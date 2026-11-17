using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Engine.Compiler.Models.Prc;
using Adamantium.Mathematics;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Writes a <see cref="PrcTestMesh"/> as a PRC_TYPE_TESS_3D_Compressed by the specification's compression
/// (Mesh Traversal, Mesh Normal Construction), its type code first.</summary>
internal sealed class PrcTestCompressedTessellation
{
    private const int AngleBits = 10;
    private const double RightAngle = Math.PI / 2;

    private readonly PrcTestMesh _mesh;
    private readonly double _tolerance;
    private readonly bool _huffman;
    private readonly bool _isRecalculated;
    private readonly PrcVector _origin;
    private readonly int[] _newIndexOf;
    private readonly List<PrcVector> _rebuilt = [];
    private readonly bool[] _treated;
    private readonly Dictionary<long, List<int>> _trianglesOfEdge = new(PairKeyComparer.Instance);
    private readonly Dictionary<long, int> _builtEdges = new(PairKeyComparer.Instance);
    private readonly Stack<PrcTestGate> _gates = new();
    private readonly List<int> _pointArray = [];
    private readonly List<int> _edgeStatus = [];
    private readonly List<int> _triangleFaces = [];
    private readonly List<bool> _isReference = [];
    private readonly List<int> _references = [];
    private readonly List<bool> _normalIsReversed = [];
    private readonly List<bool> _normalBits = [];
    private readonly List<int> _angles = [];
    private readonly List<PrcVector> _decodedNormals = [];
    private readonly Dictionary<int, List<int>> _normalsOfVertex = new();
    private readonly Dictionary<int, int> _normalOfFace = new();
    private readonly List<int> _lineAttributes = [];
    private readonly HashSet<int> _facesWithAttribute = [];
    private readonly List<int> _colors = [];
    private readonly List<bool> _textureFlags = [];
    private readonly List<int> _textureReferences = [];
    private readonly List<float> _textureParameters = [];
    private readonly Dictionary<int, List<int>> _texturesOfVertex = new();

    private PrcTestCompressedTessellation(PrcTestMesh mesh, double tolerance, bool huffman)
    {
        _mesh = mesh;
        _tolerance = tolerance;
        _huffman = huffman;
        _isRecalculated = mesh.CornerNormals == null;
        var minimum = new double[3];
        var maximum = new double[3];
        for (var axis = 0; axis < 3; axis++)
        {
            minimum[axis] = mesh.Points.Min(point => point[axis]);
            maximum[axis] = mesh.Points.Max(point => point[axis]);
        }

        _origin = new PrcVector((float)((minimum[0] + maximum[0]) / 2), (float)((minimum[1] + maximum[1]) / 2), (float)((minimum[2] + maximum[2]) / 2));
        _newIndexOf = Enumerable.Repeat(-1, mesh.Points.Count).ToArray();
        _treated = new bool[mesh.Triangles.Count];
        for (var triangle = 0; triangle < mesh.Triangles.Count; triangle++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var key = GetEdgeKey(mesh.Triangles[triangle][corner], mesh.Triangles[triangle][(corner + 1) % 3]);
                if (!_trianglesOfEdge.TryGetValue(key, out var list))
                {
                    list = [];
                    _trianglesOfEdge.Add(key, list);
                }

                list.Add(triangle);
            }
        }
    }

    /// <summary>The writer of <paramref name="mesh"/>'s compressed tessellation, every array Huffman coded or plain.</summary>
    public static Action<PrcTestBitWriter> CreateEntityWriter(PrcTestMesh mesh, double tolerance, bool huffman, bool tripleEdgeStatus = false) => writer =>
    {
        var encoder = new PrcTestCompressedTessellation(mesh, tolerance, huffman);
        encoder.Traverse();
        encoder.Write(writer, tripleEdgeStatus);
    };

    private static long GetEdgeKey(int first, int second) => ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);

    private void Traverse()
    {
        while (_treated.Any(done => !done))
        {
            while (_gates.Count > 0 && _builtEdges.GetValueOrDefault(GetEdgeKey(_gates.Peek().Lower, _gates.Peek().Higher)) > 1)
            {
                _gates.Pop();
            }

            int triangle;
            int[] traversal;
            if (_gates.Count == 0)
            {
                triangle = Array.IndexOf(_treated, false);
                var corners = _mesh.Triangles[triangle];
                var first = AddVertex(corners[0], () => _origin, null);
                var second = AddVertex(corners[1], () => _rebuilt[first], null);
                var third = AddVertex(corners[2], () => (_rebuilt[first] + _rebuilt[second]) * 0.5, null);
                traversal = first > second ? [second, first, third] : [first, second, third];
            }
            else
            {
                var gate = _gates.Pop();
                var lower = GetOriginal(gate.Lower);
                var higher = GetOriginal(gate.Higher);
                triangle = _trianglesOfEdge[GetEdgeKey(lower, higher)].First(candidate => !_treated[candidate]);
                var apex = _mesh.Triangles[triangle].First(corner => corner != lower && corner != higher);
                var apexIndex = AddVertex(apex, null, gate);
                traversal = [gate.Lower, gate.Higher, apexIndex];
            }

            _treated[triangle] = true;
            for (var corner = 0; corner < 3; corner++)
            {
                var key = GetEdgeKey(traversal[corner], traversal[(corner + 1) % 3]);
                _builtEdges[key] = _builtEdges.GetValueOrDefault(key) + 1;
            }

            var face = _mesh.Faces[triangle];
            _triangleFaces.Add(face);
            var reversed = _isRecalculated ? IsWoundAsGiven(triangle, traversal) : StoreNormals(triangle, traversal, face);
            if (_isRecalculated)
            {
                _normalIsReversed.Add(reversed);
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
                _gates.Push(right);
            }

            if (HasUntreatedNeighbor(left))
            {
                status |= 2;
                _gates.Push(left);
            }

            _edgeStatus.Add(status);
        }
    }

    private int GetOriginal(int newIndex) => Array.IndexOf(_newIndexOf, newIndex);

    private bool HasUntreatedNeighbor(PrcTestGate gate) =>
        _trianglesOfEdge.TryGetValue(GetEdgeKey(GetOriginal(gate.Lower), GetOriginal(gate.Higher)), out var list) && list.Any(candidate => !_treated[candidate]);

    private bool IsWoundAsGiven(int triangle, int[] traversal)
    {
        var given = _mesh.Triangles[triangle];
        var first = Array.IndexOf(given, GetOriginal(traversal[0]));
        return given[(first + 1) % 3] == GetOriginal(traversal[1]);
    }

    private int AddVertex(int original, Func<PrcVector> seedBase, PrcTestGate gate)
    {
        if (_newIndexOf[original] >= 0)
        {
            _isReference.Add(true);
            _references.Add(_newIndexOf[original]);
            return _newIndexOf[original];
        }

        _isReference.Add(false);
        var point = new PrcVector(_mesh.Points[original][0], _mesh.Points[original][1], _mesh.Points[original][2]);
        PrcVector placed;
        if (gate == null)
        {
            var start = seedBase();
            var offset = point - start;
            var alongX = Quantize(offset.X);
            var alongY = Quantize(offset.Y);
            var alongZ = Quantize(offset.Z);
            placed = start + new PrcVector(alongX * _tolerance, alongY * _tolerance, alongZ * _tolerance);
        }
        else
        {
            var lower = _rebuilt[gate.Lower];
            var higher = _rebuilt[gate.Higher];
            var far = _rebuilt[gate.Far];
            var center = (lower + higher) * 0.5;
            (lower - higher).TryNormalize(out var xAxis);
            if (!(far - center).Cross(xAxis).TryNormalize(out var zAxis))
            {
                xAxis.TryGetOrthogonal(out zAxis);
            }

            zAxis.Cross(xAxis).TryNormalize(out var yAxis);
            if (yAxis.Dot(center - far) > 0)
            {
                yAxis = -yAxis;
                zAxis = -zAxis;
            }

            var offset = point - center;
            var alongX = Quantize(offset.Dot(xAxis));
            var alongY = Quantize(offset.Dot(yAxis));
            var alongZ = Quantize(offset.Dot(zAxis));
            placed = center + xAxis * (alongX * _tolerance) + yAxis * (alongY * _tolerance) + zAxis * (alongZ * _tolerance);
        }

        _newIndexOf[original] = _rebuilt.Count;
        _rebuilt.Add(placed);
        return _rebuilt.Count - 1;
    }

    private int Quantize(double value)
    {
        var integer = (int)Math.Round(value / _tolerance, MidpointRounding.AwayFromZero);
        _pointArray.Add(integer);
        return integer;
    }

    private bool StoreNormals(int triangle, int[] traversal, int face)
    {
        var first = _rebuilt[traversal[0]];
        var second = _rebuilt[traversal[1]];
        var third = _rebuilt[traversal[2]];
        var given = _mesh.Triangles[triangle];
        PrcVector GetNormalAt(int corner)
        {
            var normal = _mesh.CornerNormals[triangle][Array.IndexOf(given, GetOriginal(traversal[corner]))];
            return new PrcVector(normal[0], normal[1], normal[2]);
        }

        int firstNormal;
        if (_mesh.PlanarFaces != null && _mesh.PlanarFaces[face])
        {
            if (!_normalOfFace.TryGetValue(face, out firstNormal))
            {
                firstNormal = StoreNormal(traversal[0], GetNormalAt(0), first, second, third);
                _normalOfFace.Add(face, firstNormal);
            }
        }
        else
        {
            firstNormal = StoreNormal(traversal[0], GetNormalAt(0), first, second, third);
            StoreNormal(traversal[1], GetNormalAt(1), first, second, third);
            StoreNormal(traversal[2], GetNormalAt(2), first, second, third);
        }

        return (third - (first + second) * 0.5).Cross(second - first).Dot(_decodedNormals[firstNormal]) < 0;
    }

    private int StoreNormal(int vertex, PrcVector normal, PrcVector first, PrcVector second, PrcVector third)
    {
        if (!_normalsOfVertex.TryGetValue(vertex, out var stored))
        {
            stored = [];
            _normalsOfVertex.Add(vertex, stored);
            _normalBits.Add(true);
        }
        else
        {
            var match = stored.FindLastIndex(index => _decodedNormals[index].Dot(normal) > 0.9999);
            _normalBits.Add(match >= 0);
            if (match >= 0)
            {
                var newest = stored.Count - 1;
                var back = newest - match;
                for (var bit = 0; bit < PrcBitWidths.GetUnsignedIntegerWidth(newest); bit++)
                {
                    _normalBits.Add(((back >> bit) & 1) != 0);
                }

                return stored[match];
            }
        }

        _decodedNormals.Add(EncodeNormal(normal, first, second, third));
        stored.Add(_decodedNormals.Count - 1);
        return _decodedNormals.Count - 1;
    }

    private PrcVector EncodeNormal(PrcVector normal, PrcVector first, PrcVector second, PrcVector third)
    {
        (second - first).TryNormalize(out var firstSide);
        (third - first).TryNormalize(out var secondSide);
        (third - second).TryNormalize(out var thirdSide);
        var atFirst = GetDistanceFromRightAngle(firstSide, secondSide);
        var atSecond = GetDistanceFromRightAngle(thirdSide, -firstSide);
        var atThird = GetDistanceFromRightAngle(-secondSide, -thirdSide);
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

        zAxis.TryNormalize(out zAxis);
        var reverseZ = normal.Dot(zAxis) < 0;
        if (reverseZ)
        {
            zAxis = -zAxis;
        }

        zAxis.Cross(xAxis).TryNormalize(out var yAxis);
        xAxis.TryNormalize(out xAxis);
        var reverseX = normal.Dot(xAxis) < 0;
        var reverseY = normal.Dot(yAxis) < 0;
        xAxis = reverseX ? -xAxis : xAxis;
        yAxis = reverseY ? -yAxis : yAxis;
        _normalBits.Add(reverseZ);
        _normalBits.Add(reverseX);
        _normalBits.Add(reverseY);
        var step = RightAngle / ((1 << AngleBits) - 1);
        var thetaSteps = (int)Math.Round(Math.Atan2(normal.Dot(yAxis), normal.Dot(xAxis)) / step);
        var phiSteps = (int)Math.Round(Math.Asin(Math.Clamp(normal.Dot(zAxis), -1, 1)) / step);
        _angles.Add(thetaSteps);
        _angles.Add(phiSteps);
        var theta = thetaSteps * step;
        var phi = phiSteps * step;
        return xAxis * (Math.Cos(theta) * Math.Cos(phi)) + yAxis * (Math.Sin(theta) * Math.Cos(phi)) + zAxis * Math.Sin(phi);
    }

    private static double GetDistanceFromRightAngle(PrcVector from, PrcVector to) =>
        from.TryGetAngleTo(to, out var angle) ? Math.Abs(angle - RightAngle) : RightAngle;

    private void StoreAttributes(int triangle, int[] traversal, int face)
    {
        if (_mesh.FaceLineAttributes != null && _facesWithAttribute.Add(face))
        {
            _lineAttributes.Add(_mesh.FaceLineAttributes[face]);
        }

        var given = _mesh.Triangles[triangle];
        for (var corner = 0; corner < 3; corner++)
        {
            var at = Array.IndexOf(given, GetOriginal(traversal[corner]));
            if (_mesh.CornerColors != null)
            {
                var color = _mesh.CornerColors[triangle][at];
                _colors.AddRange([1, color[0], color[1], color[2], color[3]]);
            }

            if (_mesh.CornerTextures != null)
            {
                var texture = _mesh.CornerTextures[triangle][at];
                if (!_texturesOfVertex.TryGetValue(traversal[corner], out var stored))
                {
                    stored = [];
                    _texturesOfVertex.Add(traversal[corner], stored);
                }

                var match = stored.FindIndex(index => _textureParameters[index * 2] == texture[0] && _textureParameters[index * 2 + 1] == texture[1]);
                _textureFlags.Add(match >= 0);
                if (match >= 0)
                {
                    _textureReferences.Add(match);
                    continue;
                }

                stored.Add(_textureParameters.Count / 2);
                _textureParameters.Add(texture[0]);
                _textureParameters.Add(texture[1]);
            }
        }
    }

    private void Write(PrcTestBitWriter writer, bool tripleEdgeStatus)
    {
        writer.WriteUnsignedInteger(PrcTypes.Tessellation3DCompressed);
        writer.WriteBoolean(false);
        writer.WriteBoolean(true);
        writer.WriteDouble(_tolerance);
        writer.WriteFloatAsBytes((float)_origin.X);
        writer.WriteFloatAsBytes((float)_origin.Y);
        writer.WriteFloatAsBytes((float)_origin.Z);
        PrcTestArrayWriter.WriteCompressedIntegerArray(writer, _pointArray, _huffman);
        var statuses = tripleEdgeStatus ? _edgeStatus.Concat(new int[_edgeStatus.Count * 2]).ToList() : _edgeStatus;
        PrcTestArrayWriter.WriteCharacterArray(writer, statuses, 2, _huffman);
        PrcTestArrayWriter.WriteCompressedIndiceArray(writer, _triangleFaces, _huffman);
        writer.WriteUnsignedInteger((uint)_isReference.Count);
        foreach (var flag in _isReference)
        {
            writer.WriteBoolean(flag);
        }

        PrcTestArrayWriter.WriteCompressedIndiceArray(writer, _references, _references.Count > 3, false);
        writer.WriteBoolean(_isRecalculated);
        if (_isRecalculated)
        {
            foreach (var flag in _normalIsReversed)
            {
                writer.WriteBoolean(flag);
            }

            writer.WriteDouble(0.5);
            writer.WriteCharacter(0);
        }
        else
        {
            writer.WriteCharacter(AngleBits);
            writer.WriteUnsignedInteger((uint)_normalBits.Count);
            foreach (var bit in _normalBits)
            {
                writer.WriteBoolean(bit);
            }

            PrcTestArrayWriter.WriteShortArray(writer, _angles, AngleBits, _huffman);
            for (var face = 0; face < _mesh.FaceCount; face++)
            {
                writer.WriteBoolean(_mesh.PlanarFaces != null && _mesh.PlanarFaces[face]);
            }
        }

        writer.WriteBoolean(_mesh.CornerColors != null);
        if (_mesh.CornerColors != null)
        {
            for (var face = 0; face < _mesh.FaceCount; face++)
            {
                writer.WriteBoolean(true);
            }

            PrcTestArrayWriter.WriteCharacterArray(writer, _colors, 8, _huffman);
        }

        writer.WriteBoolean(false);
        PrcTestArrayWriter.WriteShortArray(writer, _lineAttributes, 16, _huffman);
        writer.WriteBoolean(_mesh.CornerTextures == null);
        if (_mesh.CornerTextures != null)
        {
            WriteTexture(writer);
            writer.WriteBoolean(true);
        }

        writer.WriteBoolean(false);
    }

    private void WriteTexture(PrcTestBitWriter writer)
    {
        PrcTestArrayWriter.WriteWords(writer, _textureFlags);
        writer.WriteUnsignedInteger((uint)_textureReferences.Count);
        foreach (var reference in _textureReferences)
        {
            var width = PrcTestArrayWriter.CountMagnitudeBits(reference);
            writer.WriteBits((uint)width, 5);
            writer.WriteBits((uint)reference, width);
        }

        writer.WriteDouble(0);
        writer.WriteUnsignedInteger((uint)_textureParameters.Count);
        foreach (var parameter in _textureParameters)
        {
            writer.WriteFloatAsBytes(parameter);
        }
    }
}
