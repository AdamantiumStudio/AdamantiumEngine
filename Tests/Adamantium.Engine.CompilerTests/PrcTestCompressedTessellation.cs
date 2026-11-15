using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A mesh to write as a PRC highly compressed tessellation: points, triangles wound counter-clockwise around
/// their normals, the face of each triangle and, when given, a normal for each corner, colours, texture coordinates,
/// planar faces and line attributes.</summary>
internal sealed class PrcTestMesh
{
    public List<double[]> Points { get; } = [];

    public List<int[]> Triangles { get; } = [];

    public List<int> Faces { get; } = [];

    /// <summary>Three unit normals a triangle, one a corner; null to have them recalculated.</summary>
    public List<double[][]> CornerNormals { get; set; }

    /// <summary>For each face, whether it is planar - one normal for all its triangles.</summary>
    public bool[] PlanarFaces { get; set; }

    /// <summary>For each face, its style plus one; null for none.</summary>
    public int[] FaceLineAttributes { get; set; }

    /// <summary>Red, green, blue and alpha of each corner; null for none.</summary>
    public List<byte[][]> CornerColors { get; set; }

    /// <summary>U and v of each corner; null for none.</summary>
    public List<float[][]> CornerTextures { get; set; }

    public int FaceCount => Faces.Count == 0 ? 0 : Faces.Max() + 1;

    public void AddTriangle(int first, int second, int third, int face)
    {
        Triangles.Add([first, second, third]);
        Faces.Add(face);
    }
}

/// <summary>Writes a <see cref="PrcTestMesh"/> as a PRC_TYPE_TESS_3D_Compressed, its type code first, as the
/// specification's compression describes it (Mesh Traversal, Mesh points and triangles, Mesh Normal Construction):
/// the triangles are walked from a starting triangle across edges kept on a last-in-first-out stack, the right
/// neighbour pushed before the left; each new point is stored as three integers along axes built from the edge it is
/// reached across, and every later axis is built from those approximated points, as a reader rebuilds them; normals,
/// colours, line attributes and texture coordinates are stored as the triangles are met.</summary>
internal sealed class PrcTestCompressedTessellation
{
    private const int AngleBits = 10;
    private const double RightAngle = Math.PI / 2;

    private readonly PrcTestMesh mesh;
    private readonly double tolerance;
    private readonly bool huffman;
    private readonly PrcVector origin;
    private readonly int[] newIndexOf;
    private readonly List<PrcVector> rebuilt = [];
    private readonly bool[] treated;
    private readonly Dictionary<long, List<int>> trianglesOfEdge = new();
    private readonly Dictionary<long, int> builtEdges = new();
    private readonly Stack<PrcTestGate> gates = new();
    private readonly List<int> pointArray = [];
    private readonly List<int> edgeStatus = [];
    private readonly List<int> triangleFaces = [];
    private readonly List<bool> isReference = [];
    private readonly List<int> references = [];
    private readonly List<bool> normalIsReversed = [];
    private readonly List<bool> normalBits = [];
    private readonly List<int> angles = [];
    private readonly List<PrcVector> decodedNormals = [];
    private readonly Dictionary<int, List<int>> normalsOfVertex = new();
    private readonly Dictionary<int, int> normalOfFace = new();
    private readonly List<int> lineAttributes = [];
    private readonly HashSet<int> facesWithAttribute = [];
    private readonly List<int> colors = [];
    private readonly List<bool> textureFlags = [];
    private readonly List<int> textureReferences = [];
    private readonly List<float> textureParameters = [];
    private readonly Dictionary<int, List<int>> texturesOfVertex = new();

    private PrcTestCompressedTessellation(PrcTestMesh mesh, double tolerance, bool huffman)
    {
        this.mesh = mesh;
        this.tolerance = tolerance;
        this.huffman = huffman;
        var minimum = new double[3];
        var maximum = new double[3];
        for (var axis = 0; axis < 3; axis++)
        {
            minimum[axis] = mesh.Points.Min(point => point[axis]);
            maximum[axis] = mesh.Points.Max(point => point[axis]);
        }

        origin = new PrcVector((float)((minimum[0] + maximum[0]) / 2), (float)((minimum[1] + maximum[1]) / 2), (float)((minimum[2] + maximum[2]) / 2));
        newIndexOf = Enumerable.Repeat(-1, mesh.Points.Count).ToArray();
        treated = new bool[mesh.Triangles.Count];
        for (var triangle = 0; triangle < mesh.Triangles.Count; triangle++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var key = EdgeKey(mesh.Triangles[triangle][corner], mesh.Triangles[triangle][(corner + 1) % 3]);
                if (!trianglesOfEdge.TryGetValue(key, out var list))
                {
                    list = [];
                    trianglesOfEdge.Add(key, list);
                }

                list.Add(triangle);
            }
        }
    }

    /// <summary>The writer of <paramref name="mesh"/>'s compressed tessellation, every array Huffman coded or plain.</summary>
    public static Action<PrcTestBitWriter> Entity(PrcTestMesh mesh, double tolerance, bool huffman, bool tripleEdgeStatus = false) => writer =>
    {
        var encoder = new PrcTestCompressedTessellation(mesh, tolerance, huffman);
        encoder.Traverse();
        encoder.Write(writer, tripleEdgeStatus);
    };

    private bool Recalculated => mesh.CornerNormals == null;

    private static long EdgeKey(int first, int second) => ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);

    private void Traverse()
    {
        while (treated.Any(done => !done))
        {
            while (gates.Count > 0 && builtEdges.GetValueOrDefault(EdgeKey(gates.Peek().Lower, gates.Peek().Higher)) > 1)
            {
                gates.Pop();
            }

            int triangle;
            int[] traversal;
            if (gates.Count == 0)
            {
                triangle = Array.IndexOf(treated, false);
                var corners = mesh.Triangles[triangle];
                var first = Vertex(corners[0], () => origin, null);
                var second = Vertex(corners[1], () => rebuilt[first], null);
                var third = Vertex(corners[2], () => (rebuilt[first] + rebuilt[second]) * 0.5, null);
                traversal = first > second ? [second, first, third] : [first, second, third];
            }
            else
            {
                var gate = gates.Pop();
                var lower = OriginalOf(gate.Lower);
                var higher = OriginalOf(gate.Higher);
                triangle = trianglesOfEdge[EdgeKey(lower, higher)].First(candidate => !treated[candidate]);
                var apex = mesh.Triangles[triangle].First(corner => corner != lower && corner != higher);
                var apexIndex = Vertex(apex, null, gate);
                traversal = [gate.Lower, gate.Higher, apexIndex];
            }

            treated[triangle] = true;
            for (var corner = 0; corner < 3; corner++)
            {
                var key = EdgeKey(traversal[corner], traversal[(corner + 1) % 3]);
                builtEdges[key] = builtEdges.GetValueOrDefault(key) + 1;
            }

            var face = mesh.Faces[triangle];
            triangleFaces.Add(face);
            var reversed = Recalculated ? WindsAsGiven(triangle, traversal) : StoreNormals(triangle, traversal, face);
            if (Recalculated)
            {
                normalIsReversed.Add(reversed);
            }

            StoreAttributes(triangle, traversal, face);
            var acrossFirst = new PrcTestGate(traversal[0], traversal[2], traversal[1]);
            var acrossSecond = new PrcTestGate(traversal[1], traversal[2], traversal[0]);
            var left = reversed ? acrossSecond : acrossFirst;
            var right = reversed ? acrossFirst : acrossSecond;
            var status = 0;
            if (HasUntreatedNeighbor(right))
            {
                status |= 1;
                gates.Push(right);
            }

            if (HasUntreatedNeighbor(left))
            {
                status |= 2;
                gates.Push(left);
            }

            edgeStatus.Add(status);
        }
    }

    private int OriginalOf(int newIndex) => Array.IndexOf(newIndexOf, newIndex);

    private bool HasUntreatedNeighbor(PrcTestGate gate) =>
        trianglesOfEdge.TryGetValue(EdgeKey(OriginalOf(gate.Lower), OriginalOf(gate.Higher)), out var list) && list.Any(candidate => !treated[candidate]);

    private bool WindsAsGiven(int triangle, int[] traversal)
    {
        var given = mesh.Triangles[triangle];
        var first = Array.IndexOf(given, OriginalOf(traversal[0]));
        return given[(first + 1) % 3] == OriginalOf(traversal[1]);
    }

    private int Vertex(int original, Func<PrcVector> seedBase, PrcTestGate gate)
    {
        if (newIndexOf[original] >= 0)
        {
            isReference.Add(true);
            references.Add(newIndexOf[original]);
            return newIndexOf[original];
        }

        isReference.Add(false);
        var point = new PrcVector(mesh.Points[original][0], mesh.Points[original][1], mesh.Points[original][2]);
        PrcVector placed;
        if (gate == null)
        {
            var start = seedBase();
            var offset = point - start;
            var alongX = Quantize(offset.X);
            var alongY = Quantize(offset.Y);
            var alongZ = Quantize(offset.Z);
            placed = start + new PrcVector(alongX * tolerance, alongY * tolerance, alongZ * tolerance);
        }
        else
        {
            var lower = rebuilt[gate.Lower];
            var higher = rebuilt[gate.Higher];
            var far = rebuilt[gate.Far];
            var center = (lower + higher) * 0.5;
            (lower - higher).TryUnitize(out var xAxis);
            if (!(far - center).Cross(xAxis).TryUnitize(out var zAxis))
            {
                xAxis.TryOrthogonal(out zAxis);
            }

            zAxis.Cross(xAxis).TryUnitize(out var yAxis);
            if (yAxis.Dot(center - far) > 0)
            {
                yAxis = -yAxis;
                zAxis = -zAxis;
            }

            var offset = point - center;
            var alongX = Quantize(offset.Dot(xAxis));
            var alongY = Quantize(offset.Dot(yAxis));
            var alongZ = Quantize(offset.Dot(zAxis));
            placed = center + xAxis * (alongX * tolerance) + yAxis * (alongY * tolerance) + zAxis * (alongZ * tolerance);
        }

        newIndexOf[original] = rebuilt.Count;
        rebuilt.Add(placed);
        return rebuilt.Count - 1;
    }

    private int Quantize(double value)
    {
        var integer = (int)Math.Round(value / tolerance, MidpointRounding.AwayFromZero);
        pointArray.Add(integer);
        return integer;
    }

    private bool StoreNormals(int triangle, int[] traversal, int face)
    {
        var first = rebuilt[traversal[0]];
        var second = rebuilt[traversal[1]];
        var third = rebuilt[traversal[2]];
        var given = mesh.Triangles[triangle];
        PrcVector NormalAt(int corner)
        {
            var normal = mesh.CornerNormals[triangle][Array.IndexOf(given, OriginalOf(traversal[corner]))];
            return new PrcVector(normal[0], normal[1], normal[2]);
        }

        int firstNormal;
        if (mesh.PlanarFaces != null && mesh.PlanarFaces[face])
        {
            if (!normalOfFace.TryGetValue(face, out firstNormal))
            {
                firstNormal = StoreNormal(traversal[0], NormalAt(0), first, second, third);
                normalOfFace.Add(face, firstNormal);
            }
        }
        else
        {
            firstNormal = StoreNormal(traversal[0], NormalAt(0), first, second, third);
            StoreNormal(traversal[1], NormalAt(1), first, second, third);
            StoreNormal(traversal[2], NormalAt(2), first, second, third);
        }

        return (third - (first + second) * 0.5).Cross(second - first).Dot(decodedNormals[firstNormal]) < 0;
    }

    private int StoreNormal(int vertex, PrcVector normal, PrcVector first, PrcVector second, PrcVector third)
    {
        if (!normalsOfVertex.TryGetValue(vertex, out var stored))
        {
            stored = [];
            normalsOfVertex.Add(vertex, stored);
            normalBits.Add(true);
        }
        else
        {
            var match = stored.FindLastIndex(index => decodedNormals[index].Dot(normal) > 0.9999);
            normalBits.Add(match >= 0);
            if (match >= 0)
            {
                var newest = stored.Count - 1;
                var back = newest - match;
                for (var bit = 0; bit < PrcBitWidths.UnsignedInteger(newest); bit++)
                {
                    normalBits.Add(((back >> bit) & 1) != 0);
                }

                return stored[match];
            }
        }

        decodedNormals.Add(EncodeNormal(normal, first, second, third));
        stored.Add(decodedNormals.Count - 1);
        return decodedNormals.Count - 1;
    }

    private PrcVector EncodeNormal(PrcVector normal, PrcVector first, PrcVector second, PrcVector third)
    {
        (second - first).TryUnitize(out var firstSide);
        (third - first).TryUnitize(out var secondSide);
        (third - second).TryUnitize(out var thirdSide);
        var atFirst = OffRightAngle(firstSide, secondSide);
        var atSecond = OffRightAngle(thirdSide, -firstSide);
        var atThird = OffRightAngle(-secondSide, -thirdSide);
        PrcVector xAxis;
        PrcVector zAxis;
        if (atFirst < atSecond && atFirst < atThird)
        {
            xAxis = firstSide;
            zAxis = firstSide.Cross(secondSide);
        }
        else if (atSecond < atThird)
        {
            xAxis = thirdSide;
            zAxis = (-thirdSide).Cross(firstSide);
        }
        else
        {
            xAxis = -secondSide;
            zAxis = secondSide.Cross(thirdSide);
        }

        zAxis.TryUnitize(out zAxis);
        var reverseZ = normal.Dot(zAxis) < 0;
        if (reverseZ)
        {
            zAxis = -zAxis;
        }

        zAxis.Cross(xAxis).TryUnitize(out var yAxis);
        xAxis.TryUnitize(out xAxis);
        var reverseX = normal.Dot(xAxis) < 0;
        var reverseY = normal.Dot(yAxis) < 0;
        xAxis = reverseX ? -xAxis : xAxis;
        yAxis = reverseY ? -yAxis : yAxis;
        normalBits.Add(reverseZ);
        normalBits.Add(reverseX);
        normalBits.Add(reverseY);
        var step = RightAngle / ((1 << AngleBits) - 1);
        var thetaSteps = (int)Math.Round(Math.Atan2(normal.Dot(yAxis), normal.Dot(xAxis)) / step);
        var phiSteps = (int)Math.Round(Math.Asin(Math.Clamp(normal.Dot(zAxis), -1, 1)) / step);
        angles.Add(thetaSteps);
        angles.Add(phiSteps);
        var theta = thetaSteps * step;
        var phi = phiSteps * step;
        return xAxis * (Math.Cos(theta) * Math.Cos(phi)) + yAxis * (Math.Sin(theta) * Math.Cos(phi)) + zAxis * Math.Sin(phi);
    }

    private static double OffRightAngle(PrcVector from, PrcVector to) =>
        from.TryAngleTo(to, out var angle) ? Math.Abs(angle - RightAngle) : RightAngle;

    private void StoreAttributes(int triangle, int[] traversal, int face)
    {
        if (mesh.FaceLineAttributes != null && facesWithAttribute.Add(face))
        {
            lineAttributes.Add(mesh.FaceLineAttributes[face]);
        }

        var given = mesh.Triangles[triangle];
        for (var corner = 0; corner < 3; corner++)
        {
            var at = Array.IndexOf(given, OriginalOf(traversal[corner]));
            if (mesh.CornerColors != null)
            {
                var color = mesh.CornerColors[triangle][at];
                colors.AddRange([1, color[0], color[1], color[2], color[3]]);
            }

            if (mesh.CornerTextures != null)
            {
                var texture = mesh.CornerTextures[triangle][at];
                if (!texturesOfVertex.TryGetValue(traversal[corner], out var stored))
                {
                    stored = [];
                    texturesOfVertex.Add(traversal[corner], stored);
                }

                var match = stored.FindIndex(index => textureParameters[index * 2] == texture[0] && textureParameters[index * 2 + 1] == texture[1]);
                textureFlags.Add(match >= 0);
                if (match >= 0)
                {
                    textureReferences.Add(match);
                    continue;
                }

                stored.Add(textureParameters.Count / 2);
                textureParameters.Add(texture[0]);
                textureParameters.Add(texture[1]);
            }
        }
    }

    private void Write(PrcTestBitWriter writer, bool tripleEdgeStatus)
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3DCompressed);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteDouble(tolerance);
        writer.WriteFloatAsBytes((float)origin.X);
        writer.WriteFloatAsBytes((float)origin.Y);
        writer.WriteFloatAsBytes((float)origin.Z);
        PrcTestArrayWriter.CompressedIntegerArray(writer, pointArray, huffman);
        var statuses = tripleEdgeStatus ? edgeStatus.Concat(new int[edgeStatus.Count * 2]).ToList() : edgeStatus;
        PrcTestArrayWriter.CharacterArray(writer, statuses, 2, huffman);
        PrcTestArrayWriter.CompressedIndiceArray(writer, triangleFaces, huffman);
        writer.WriteUnsignedInteger((uint)isReference.Count);
        foreach (var flag in isReference)
        {
            writer.WriteBoolean(flag);
        }

        PrcTestArrayWriter.CompressedIndiceArray(writer, references, references.Count > 3, false);
        writer.WriteBoolean(Recalculated);
        if (Recalculated)
        {
            foreach (var flag in normalIsReversed)
            {
                writer.WriteBoolean(flag);
            }

            writer.WriteDouble(0.5);
            writer.WriteCharacter(0);
        }
        else
        {
            writer.WriteCharacter(AngleBits);
            writer.WriteUnsignedInteger((uint)normalBits.Count);
            foreach (var bit in normalBits)
            {
                writer.WriteBoolean(bit);
            }

            PrcTestArrayWriter.ShortArray(writer, angles, AngleBits, huffman);
            for (var face = 0; face < mesh.FaceCount; face++)
            {
                writer.WriteBoolean(mesh.PlanarFaces != null && mesh.PlanarFaces[face]);
            }
        }

        writer.WriteBoolean(mesh.CornerColors != null);
        if (mesh.CornerColors != null)
        {
            for (var face = 0; face < mesh.FaceCount; face++)
            {
                writer.WriteBoolean(true);
            }

            PrcTestArrayWriter.CharacterArray(writer, colors, 8, huffman);
        }

        writer.WriteBoolean(false);
        PrcTestArrayWriter.ShortArray(writer, lineAttributes, 16, huffman);
        writer.WriteBoolean(mesh.CornerTextures == null);
        if (mesh.CornerTextures != null)
        {
            WriteTexture(writer);
            writer.WriteBoolean(true);
        }

        writer.WriteBoolean(false);
    }

    private void WriteTexture(PrcTestBitWriter writer)
    {
        var words = (textureFlags.Count + 31) / 32;
        writer.WriteUnsignedInteger((uint)words);
        for (var part = 0; part < words * 4; part++)
        {
            var character = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                var at = part * 8 + bit;
                if (at < textureFlags.Count && textureFlags[at])
                {
                    character |= 1 << bit;
                }
            }

            writer.WriteCharacter((byte)character);
        }

        writer.WriteUnsignedInteger((uint)(words == 0 ? 0 : textureFlags.Count - (words - 1) * 32));
        writer.WriteUnsignedInteger((uint)textureReferences.Count);
        foreach (var reference in textureReferences)
        {
            var width = PrcTestArrayWriter.MagnitudeBits(reference);
            writer.WriteBits((uint)width, 5);
            writer.WriteBits((uint)reference, width);
        }

        writer.WriteDouble(0);
        writer.WriteUnsignedInteger((uint)textureParameters.Count);
        foreach (var parameter in textureParameters)
        {
            writer.WriteFloatAsBytes(parameter);
        }
    }
}

/// <summary>An edge of the test traversal: its two points by index, the earlier first, and the third point of the
/// triangle that pushed it.</summary>
internal sealed class PrcTestGate
{
    public PrcTestGate(int first, int second, int far)
    {
        Lower = Math.Min(first, second);
        Higher = Math.Max(first, second);
        Far = far;
    }

    public int Lower { get; }

    public int Higher { get; }

    public int Far { get; }
}
