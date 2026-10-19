// Ported from Draco (src/draco/compression/mesh/mesh_edgebreaker_decoder_impl.cc,
// mesh_edgebreaker_traversal_decoder.h, mesh_edgebreaker_traversal_valence_decoder.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoEdgebreaker
{
    private const uint SymbolC = 0;
    private const uint SymbolS = 1;
    private const uint SymbolL = 3;
    private const uint SymbolR = 5;
    private const uint SymbolE = 7;
    private const byte StandardTraversal = 0;
    private const byte ValenceTraversal = 2;
    private const int MinValence = 2;
    private const int MaxValence = 7;
    private const byte VertexAttribute = 0;
    private const byte ByDegreeTraversal = 1;

    private static readonly uint[] SymbolTopology = [SymbolC, SymbolS, SymbolL, SymbolR, SymbolE];

    private readonly DracoReader reader;
    private readonly bool valence;
    private readonly List<(uint Source, uint Split, bool RightEdge)> splits = [];
    private readonly DracoEncodingData positionEncoding = new();
    private DracoCornerTable table;
    private AttributeData[] attributeData = [];
    private bool[] vertexIsHole = [];
    private DracoReader symbols;
    private readonly DracoBitDecoder startFaces = new();
    private DracoBitDecoder[] seamDecoders = [];
    private int[] valences = [];
    private uint[][] contextSymbols = [];
    private int[] contextCounters = [];
    private int activeContext = -1;
    private uint lastSymbol;
    private int positionDecoder = -1;

    private DracoEdgebreaker(DracoReader reader, bool valence)
    {
        this.reader = reader;
        this.valence = valence;
    }

    public int[] Faces { get; private set; } = [];

    public int PointCount { get; private set; }

    public static DracoEdgebreaker Decode(DracoReader reader, int maxFaces)
    {
        var traversal = reader.Byte();
        if (traversal is not (StandardTraversal or ValenceTraversal))
        {
            throw new InvalidDataException($"a Draco edgebreaker traversal of kind {traversal}");
        }

        var edgebreaker = new DracoEdgebreaker(reader, traversal == ValenceTraversal);
        edgebreaker.DecodeConnectivity(maxFaces);
        return edgebreaker;
    }

    public DracoSequencer CreateSequencer(int decoder)
    {
        var dataId = reader.SByte();
        var decoderType = reader.Byte();
        if (dataId >= 0)
        {
            if (dataId >= attributeData.Length || attributeData[dataId].Decoder >= 0)
            {
                throw new InvalidDataException("a Draco attribute decoder of other connectivity");
            }

            attributeData[dataId].Decoder = decoder;
        }
        else
        {
            if (positionDecoder >= 0)
            {
                throw new InvalidDataException("two Draco attribute decoders of the position connectivity");
            }

            positionDecoder = decoder;
        }

        var method = reader.Byte();
        if (method > ByDegreeTraversal)
        {
            throw new InvalidDataException($"a Draco attribute traversal of kind {method}");
        }

        if (decoderType == VertexAttribute)
        {
            var encoding = dataId < 0 ? positionEncoding : attributeData[dataId].Encoding;
            return new DracoSequencer(table, encoding, method == ByDegreeTraversal);
        }

        if (method != 0 || dataId < 0)
        {
            throw new InvalidDataException("a Draco corner attribute decoder of no connectivity of its own");
        }

        return new DracoSequencer(attributeData[dataId].Seams, attributeData[dataId].Encoding, false);
    }

    private void DecodeConnectivity(int maxFaces)
    {
        var encodedVertices = reader.VarUInt32();
        var faces = reader.VarUInt32();
        if (faces > int.MaxValue / 3 || encodedVertices > (ulong)faces * 3)
        {
            throw new InvalidDataException($"a Draco mesh of {faces} faces and {encodedVertices} vertices");
        }

        if ((ulong)encodedVertices * (encodedVertices - 1) / 2 < 3 * faces / 2)
        {
            throw new InvalidDataException($"no manifold Draco mesh has {faces} faces on {encodedVertices} vertices");
        }

        var dataCount = reader.Byte();
        var symbolCount = reader.VarUInt32();
        if (faces < symbolCount || faces > symbolCount + symbolCount / 3)
        {
            throw new InvalidDataException($"a Draco mesh of {faces} faces from {symbolCount} symbols");
        }

        var splitSymbols = reader.VarUInt32();
        if (splitSymbols > symbolCount)
        {
            throw new InvalidDataException("more Draco split symbols than symbols");
        }

        if (faces > maxFaces || encodedVertices + (long)splitSymbols > Array.MaxLength)
        {
            throw new InvalidDataException($"a Draco mesh of {faces} faces and {encodedVertices + (long)splitSymbols} " +
                                           $"vertices, {maxFaces} faces at most are expected");
        }

        var vertexCount = (int)(encodedVertices + splitSymbols);
        attributeData = new AttributeData[dataCount];
        for (var i = 0; i < dataCount; i++)
        {
            attributeData[i] = new AttributeData();
        }

        DecodeSplits(faces);
        var end = StartTraversal(faces, symbolCount, vertexCount);
        table = new DracoCornerTable((int)faces);
        vertexIsHole = new bool[vertexCount];
        Array.Fill(vertexIsHole, true);
        var vertices = DecodeFaces((int)symbolCount);
        reader.MoveTo(end);
        if (attributeData.Length > 0)
        {
            for (var corner = 0; corner < table.CornerCount; corner += 3)
            {
                DecodeSeams(corner);
            }
        }

        foreach (var data in attributeData)
        {
            data.Seams = new DracoSeamCornerTable(table);
            foreach (var corner in data.SeamCorners)
            {
                data.Seams.AddSeamEdge(corner);
            }

            data.Seams.RecomputeVertices();
        }

        positionEncoding.Init(table.VertexCount);
        foreach (var data in attributeData)
        {
            data.Encoding.Init(Math.Max(data.Seams.VertexCount, table.VertexCount));
        }

        AssignPoints(vertices);
    }

    private void DecodeSplits(uint faces)
    {
        var count = reader.VarUInt32();
        if (count == 0)
        {
            return;
        }

        if (count > faces)
        {
            throw new InvalidDataException("more Draco topology splits than faces");
        }

        var last = 0u;
        for (var i = 0; i < count; i++)
        {
            var source = reader.VarUInt32() + last;
            var delta = reader.VarUInt32();
            if (delta > source)
            {
                throw new InvalidDataException("a Draco topology split before its source");
            }

            splits.Add((source, source - delta, false));
            last = source;
        }

        reader.StartBits(false);
        for (var i = 0; i < count; i++)
        {
            splits[i] = splits[i] with { RightEdge = (reader.Bits(1) & 1) != 0 };
        }

        reader.EndBits();
    }

    private DracoReader StartTraversal(uint faces, uint symbolCount, int vertexCount)
    {
        var end = reader.Copy();
        if (!valence)
        {
            symbols = reader.Copy();
            var size = symbols.StartBits(true);
            if (size > (ulong)symbols.Remaining || size * 8 < symbolCount)
            {
                throw new InvalidDataException($"{symbolCount} Draco edgebreaker symbols in {size} bytes");
            }

            end = symbols.Copy();
            end.Skip((long)size);
        }

        startFaces.Start(end);
        seamDecoders = new DracoBitDecoder[attributeData.Length];
        for (var i = 0; i < seamDecoders.Length; i++)
        {
            seamDecoders[i] = new DracoBitDecoder();
            seamDecoders[i].Start(end);
        }

        if (valence)
        {
            valences = new int[vertexCount];
            contextSymbols = new uint[MaxValence - MinValence + 1][];
            contextCounters = new int[contextSymbols.Length];
            for (var i = 0; i < contextSymbols.Length; i++)
            {
                var count = end.VarUInt32();
                if (count > faces)
                {
                    throw new InvalidDataException("more Draco valence symbols than faces");
                }

                contextSymbols[i] = new uint[count];
                DracoSymbols.Decode(end, (int)count, 1, contextSymbols[i]);
                contextCounters[i] = (int)count;
            }
        }

        return end;
    }

    private uint NextSymbol()
    {
        if (!valence)
        {
            if (symbols.Bits(1) == SymbolC)
            {
                return SymbolC;
            }

            return 1 | (symbols.Bits(2) << 1);
        }

        if (activeContext == -1)
        {
            lastSymbol = SymbolE;
            return lastSymbol;
        }

        var counter = --contextCounters[activeContext];
        if (counter < 0 || contextSymbols[activeContext][counter] > 4)
        {
            throw new InvalidDataException("a Draco valence context runs out of symbols");
        }

        lastSymbol = SymbolTopology[contextSymbols[activeContext][counter]];
        return lastSymbol;
    }

    private void ReachCorner(int corner)
    {
        if (!valence)
        {
            return;
        }

        var next = DracoCorners.Next(corner);
        var previous = DracoCorners.Previous(corner);
        switch (lastSymbol)
        {
            case SymbolC or SymbolS:
                valences[table.Vertex(next)] += 1;
                valences[table.Vertex(previous)] += 1;
                break;
            case SymbolR:
                valences[table.Vertex(corner)] += 1;
                valences[table.Vertex(next)] += 1;
                valences[table.Vertex(previous)] += 2;
                break;
            case SymbolL:
                valences[table.Vertex(corner)] += 1;
                valences[table.Vertex(next)] += 2;
                valences[table.Vertex(previous)] += 1;
                break;
            case SymbolE:
                valences[table.Vertex(corner)] += 2;
                valences[table.Vertex(next)] += 2;
                valences[table.Vertex(previous)] += 2;
                break;
        }

        activeContext = Math.Clamp(valences[table.Vertex(next)], MinValence, MaxValence) - MinValence;
    }

    private int DecodeFaces(int symbolCount)
    {
        var active = new List<int>();
        var splitCorners = new Dictionary<int, int>();
        var isolated = new List<int>();
        var removeIsolated = attributeData.Length == 0;
        var maxVertices = vertexIsHole.Length;
        var faces = 0;
        for (var symbolId = 0; symbolId < symbolCount; symbolId++)
        {
            var corner = 3 * faces++;
            var checkSplit = false;
            switch (NextSymbol())
            {
                case SymbolC:
                    DecodeC(active, corner);
                    break;
                case SymbolR:
                    DecodeLeftOrRight(active, corner, true, maxVertices);
                    checkSplit = true;
                    break;
                case SymbolL:
                    DecodeLeftOrRight(active, corner, false, maxVertices);
                    checkSplit = true;
                    break;
                case SymbolS:
                    DecodeS(active, corner, splitCorners, symbolId, removeIsolated ? isolated : null);
                    break;
                case SymbolE:
                    DecodeE(active, corner, maxVertices);
                    checkSplit = true;
                    break;
                default:
                    throw new InvalidDataException("a Draco edgebreaker symbol of no known kind");
            }

            ReachCorner(active[^1]);
            if (checkSplit)
            {
                var encoderSymbol = symbolCount - symbolId - 1;
                while (TakeSplit(encoderSymbol, out var rightEdge, out var encoderSplit))
                {
                    var top = active[^1];
                    splitCorners[symbolCount - encoderSplit - 1] = rightEdge ? DracoCorners.Next(top) : DracoCorners.Previous(top);
                }
            }
        }

        if (table.VertexCount > maxVertices)
        {
            throw new InvalidDataException("more Draco vertices than encoded");
        }

        while (active.Count > 0)
        {
            var corner = active[^1];
            active.RemoveAt(active.Count - 1);
            if (startFaces.Next())
            {
                faces = DecodeInteriorStartFace(corner, faces);
            }
        }

        if (faces != table.FaceCount)
        {
            throw new InvalidDataException($"{faces} Draco faces decoded of {table.FaceCount}");
        }

        return RemoveIsolated(isolated);
    }

    private void DecodeC(List<int> active, int corner)
    {
        var a = Top(active);
        var x = table.Vertex(DracoCorners.Next(a));
        var b = DracoCorners.Next(table.LeftMostCorner(x));
        RequireOpenPair(a, b);

        table.SetOpposites(a, corner + 1);
        table.SetOpposites(b, corner + 2);
        var aPrevious = table.Vertex(DracoCorners.Previous(a));
        var bNext = table.Vertex(DracoCorners.Next(b));
        if (x == aPrevious || x == bNext)
        {
            throw Broken();
        }

        table.MapCornerToVertex(corner, x);
        table.MapCornerToVertex(corner + 1, bNext);
        table.MapCornerToVertex(corner + 2, aPrevious);
        table.SetLeftMostCorner(aPrevious, corner + 2);
        vertexIsHole[x] = false;
        active[^1] = corner;
    }

    private void DecodeLeftOrRight(List<int> active, int corner, bool right, int maxVertices)
    {
        var a = Top(active);
        if (table.Opposite(a) != DracoCorners.Invalid)
        {
            throw Broken();
        }

        var opposite = right ? corner + 2 : corner + 1;
        var cornerL = right ? corner + 1 : corner;
        var cornerR = right ? corner : corner + 2;
        table.SetOpposites(opposite, a);
        var vertex = table.AddVertex();
        if (table.VertexCount > maxVertices)
        {
            throw new InvalidDataException("more Draco vertices than encoded");
        }

        table.MapCornerToVertex(opposite, vertex);
        table.SetLeftMostCorner(vertex, opposite);
        var vertexR = table.Vertex(DracoCorners.Previous(a));
        table.MapCornerToVertex(cornerR, vertexR);
        table.SetLeftMostCorner(vertexR, cornerR);
        table.MapCornerToVertex(cornerL, table.Vertex(DracoCorners.Next(a)));
        active[^1] = corner;
    }

    private void DecodeS(List<int> active, int corner, Dictionary<int, int> splitCorners, int symbolId, List<int> isolated)
    {
        var b = Top(active);
        active.RemoveAt(active.Count - 1);
        if (splitCorners.TryGetValue(symbolId, out var split))
        {
            active.Add(split);
        }

        var a = Top(active);
        RequireOpenPair(a, b);

        table.SetOpposites(a, corner + 2);
        table.SetOpposites(b, corner + 1);
        var p = table.Vertex(DracoCorners.Previous(a));
        table.MapCornerToVertex(corner, p);
        table.MapCornerToVertex(corner + 1, table.Vertex(DracoCorners.Next(a)));
        var bPrevious = table.Vertex(DracoCorners.Previous(b));
        table.MapCornerToVertex(corner + 2, bPrevious);
        table.SetLeftMostCorner(bPrevious, corner + 2);
        var cornerN = DracoCorners.Next(b);
        var n = table.Vertex(cornerN);
        if (valence)
        {
            valences[p] += valences[n];
        }

        table.SetLeftMostCorner(p, table.LeftMostCorner(n));
        var first = cornerN;
        while (cornerN != DracoCorners.Invalid)
        {
            table.MapCornerToVertex(cornerN, p);
            cornerN = table.SwingLeft(cornerN);
            if (cornerN == first)
            {
                throw Broken();
            }
        }

        table.SetLeftMostCorner(n, DracoCorners.Invalid);
        isolated?.Add(n);
        active[^1] = corner;
    }

    private void DecodeE(List<int> active, int corner, int maxVertices)
    {
        var first = table.AddVertex();
        table.MapCornerToVertex(corner, first);
        table.MapCornerToVertex(corner + 1, table.AddVertex());
        table.MapCornerToVertex(corner + 2, table.AddVertex());
        if (table.VertexCount > maxVertices)
        {
            throw new InvalidDataException("more Draco vertices than encoded");
        }

        table.SetLeftMostCorner(first, corner);
        table.SetLeftMostCorner(first + 1, corner + 1);
        table.SetLeftMostCorner(first + 2, corner + 2);
        active.Add(corner);
    }

    private int DecodeInteriorStartFace(int corner, int faces)
    {
        if (faces >= table.FaceCount)
        {
            throw new InvalidDataException("more Draco faces than encoded");
        }

        var n = table.Vertex(DracoCorners.Next(corner));
        var b = DracoCorners.Next(table.LeftMostCorner(n));
        var x = table.Vertex(DracoCorners.Next(b));
        var c = DracoCorners.Next(table.LeftMostCorner(x));
        RequireOpenPair(corner, b);
        RequireOpenPair(b, c);
        RequireOpenPair(c, corner);

        var p = table.Vertex(DracoCorners.Next(c));
        var start = 3 * faces;
        table.SetOpposites(start, corner);
        table.SetOpposites(start + 1, b);
        table.SetOpposites(start + 2, c);
        table.MapCornerToVertex(start, x);
        table.MapCornerToVertex(start + 1, p);
        table.MapCornerToVertex(start + 2, n);
        for (var i = 0; i < 3; i++)
        {
            vertexIsHole[table.Vertex(start + i)] = false;
        }

        return faces + 1;
    }

    private int RemoveIsolated(List<int> isolated)
    {
        var vertices = table.VertexCount;
        foreach (var vertex in isolated)
        {
            var source = vertices - 1;
            while (table.LeftMostCorner(source) == DracoCorners.Invalid)
            {
                source = --vertices - 1;
                if (source < 0)
                {
                    throw Broken();
                }
            }

            if (source < vertex)
            {
                continue;
            }

            foreach (var corner in table.CornersAround(table.LeftMostCorner(source)))
            {
                if (table.Vertex(corner) != source)
                {
                    throw Broken();
                }

                table.MapCornerToVertex(corner, vertex);
            }

            table.SetLeftMostCorner(vertex, table.LeftMostCorner(source));
            table.SetLeftMostCorner(source, DracoCorners.Invalid);
            vertexIsHole[vertex] = vertexIsHole[source];
            vertexIsHole[source] = false;
            vertices--;
        }

        return vertices;
    }

    private bool TakeSplit(int encoderSymbol, out bool rightEdge, out int encoderSplit)
    {
        rightEdge = false;
        encoderSplit = 0;
        if (splits.Count == 0)
        {
            return false;
        }

        var last = splits[^1];
        if (last.Source > (uint)encoderSymbol)
        {
            throw new InvalidDataException("a Draco topology split of a symbol already decoded");
        }

        if (last.Source != (uint)encoderSymbol)
        {
            return false;
        }

        rightEdge = last.RightEdge;
        encoderSplit = (int)last.Split;
        splits.RemoveAt(splits.Count - 1);
        if (encoderSplit < 0)
        {
            throw new InvalidDataException("a Draco topology split of no symbol");
        }

        return true;
    }

    private void DecodeSeams(int corner)
    {
        Span<int> corners = [corner, DracoCorners.Next(corner), DracoCorners.Previous(corner)];
        foreach (var c in corners)
        {
            var opposite = table.Opposite(c);
            if (opposite == DracoCorners.Invalid)
            {
                foreach (var data in attributeData)
                {
                    data.SeamCorners.Add(c);
                }

                continue;
            }

            if (opposite / 3 < corner / 3)
            {
                continue;
            }

            for (var i = 0; i < attributeData.Length; i++)
            {
                if (seamDecoders[i].Next())
                {
                    attributeData[i].SeamCorners.Add(c);
                }
            }
        }
    }

    private void AssignPoints(int vertices)
    {
        if (attributeData.Length == 0)
        {
            Faces = new int[table.CornerCount];
            for (var corner = 0; corner < Faces.Length; corner++)
            {
                Faces[corner] = table.Vertex(corner);
            }

            PointCount = vertices;
            return;
        }

        Faces = new int[table.CornerCount];
        var points = 0;
        for (var v = 0; v < table.VertexCount; v++)
        {
            var start = table.LeftMostCorner(v);
            if (start == DracoCorners.Invalid)
            {
                continue;
            }

            var first = vertexIsHole[v] ? start : FirstSeamCorner(start);
            Faces[first] = points++;
            var previous = first;
            var corner = table.SwingRight(first);
            while (corner != DracoCorners.Invalid && corner != first)
            {
                Faces[corner] = IsSeamBetween(previous, corner) ? points++ : Faces[previous];
                previous = corner;
                corner = table.SwingRight(corner);
            }
        }

        PointCount = points;
    }

    private int FirstSeamCorner(int start)
    {
        foreach (var data in attributeData)
        {
            if (!data.Seams.IsCornerOnSeam(start))
            {
                continue;
            }

            var vertex = data.Seams.Vertex(start);
            var corner = table.SwingRight(start);
            while (corner != start)
            {
                if (corner == DracoCorners.Invalid)
                {
                    throw Broken();
                }

                if (data.Seams.Vertex(corner) != vertex)
                {
                    return corner;
                }

                corner = table.SwingRight(corner);
            }
        }

        return start;
    }

    private bool IsSeamBetween(int previous, int corner)
    {
        foreach (var data in attributeData)
        {
            if (data.Seams.Vertex(corner) != data.Seams.Vertex(previous))
            {
                return true;
            }
        }

        return false;
    }

    private void RequireOpenPair(int a, int b)
    {
        if (a == b || table.Opposite(a) != DracoCorners.Invalid || table.Opposite(b) != DracoCorners.Invalid)
        {
            throw Broken();
        }
    }

    private static int Top(List<int> active)
    {
        if (active.Count == 0)
        {
            throw Broken();
        }

        return active[^1];
    }

    private static InvalidDataException Broken() => new("Draco edgebreaker data is broken");

    private sealed class AttributeData
    {
        public int Decoder { get; set; } = -1;

        public DracoSeamCornerTable Seams { get; set; }

        public DracoEncodingData Encoding { get; } = new();

        public List<int> SeamCorners { get; } = [];
    }
}
