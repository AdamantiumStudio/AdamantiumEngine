using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the normals of a highly compressed tessellation as its triangles are built (PRC Mesh Normal
/// Description and Construction).</summary>
internal sealed class PrcCompressedNormals
{
    private const double RightAngle = Math.PI / 2;
    private const double EighthTurn = Math.PI / 4;
    private const int LongestAngle = 16;

    private readonly PrcCompressedTessellationData _data;
    private readonly PrcBitReader _bits;
    private readonly Dictionary<int, PrcNormalSlot> _vertexSlots = new();
    private readonly Dictionary<int, int> _faceNormals = new();
    private readonly double _angleStep;
    private int _bitCursor;
    private int _angleCursor;

    internal PrcCompressedNormals(PrcCompressedTessellationData data, PrcBitReader bits)
    {
        _data = data;
        _bits = bits;
        if (data.NormalAngleBits < 1 || data.NormalAngleBits > LongestAngle)
        {
            throw bits.CreateCorruptException($"a compressed tessellation stores its normal angles in {data.NormalAngleBits} bits");
        }

        _angleStep = RightAngle / ((1 << data.NormalAngleBits) - 1);
    }

    /// <summary>Every normal stored, in order.</summary>
    internal List<PrcVector> All { get; } = [];

    /// <summary>Whether every flag and angle stored has been read.</summary>
    internal bool IsAtEnd => _bitCursor == _data.NormalBinaryData.Length && _angleCursor == _data.NormalAngles.Length;

    internal PrcVector GetNormal(int index) => All[index];

    /// <summary>Reads the normals of <paramref name="triangle"/>, whose vertices are placed, into its normal indices.</summary>
    internal void Restore(PrcDecodedTriangle triangle, IReadOnlyList<PrcVector> points)
    {
        var first = points[triangle.First];
        var second = points[triangle.Second];
        var third = points[triangle.Third];
        if (triangle.Face < _data.IsFacePlanar.Length && _data.IsFacePlanar[triangle.Face])
        {
            if (!_faceNormals.TryGetValue(triangle.Face, out var shared))
            {
                shared = Restore(GetSlot(triangle.First), first, second, third);
                _faceNormals.Add(triangle.Face, shared);
            }

            triangle.FirstNormal = shared;
            triangle.SecondNormal = shared;
            triangle.ThirdNormal = shared;
            return;
        }

        triangle.FirstNormal = Restore(GetSlot(triangle.First), first, second, third);
        triangle.SecondNormal = Restore(GetSlot(triangle.Second), first, second, third);
        triangle.ThirdNormal = Restore(GetSlot(triangle.Third), first, second, third);
    }

    private PrcNormalSlot GetSlot(int vertex)
    {
        if (!_vertexSlots.TryGetValue(vertex, out var slot))
        {
            slot = new PrcNormalSlot();
            _vertexSlots.Add(vertex, slot);
        }

        return slot;
    }

    private int Restore(PrcNormalSlot slot, PrcVector first, PrcVector second, PrcVector third)
    {
        if (!slot.IsMet)
        {
            slot.IsMet = true;
            slot.HasSeveral = ReadBit();
        }
        else if (!slot.HasSeveral)
        {
            return slot.Normals[0];
        }
        else if (ReadBit())
        {
            var newest = slot.Normals.Count - 1;
            var back = 0;
            for (var bit = 0; bit < PrcBitWidths.GetUnsignedIntegerWidth(newest); bit++)
            {
                if (ReadBit())
                {
                    back |= 1 << bit;
                }
            }

            if (back > newest)
            {
                throw _bits.CreateCorruptException($"a normal of a compressed tessellation refers {back} back of the {slot.Normals.Count} its vertex stored");
            }

            return slot.Normals[newest - back];
        }

        All.Add(ReadNormal(first, second, third));
        slot.Normals.Add(All.Count - 1);
        return All.Count - 1;
    }

    private PrcVector ReadNormal(PrcVector first, PrcVector second, PrcVector third)
    {
        var reverseZ = ReadBit();
        var reverseX = ReadBit();
        var reverseY = ReadBit();
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

        if (!zAxis.TryNormalize(out zAxis))
        {
            xAxis.TryGetOrthogonal(out zAxis);
        }

        if (reverseZ)
        {
            zAxis = -zAxis;
        }

        zAxis.Cross(xAxis).TryNormalize(out var yAxis);
        xAxis.TryNormalize(out xAxis);
        if (reverseX)
        {
            xAxis = -xAxis;
        }

        if (reverseY)
        {
            yAxis = -yAxis;
        }

        var theta = ReadAngle();
        var phi = ReadAngle();
        var cosinePhi = GetCosine(phi);
        return xAxis * (GetCosine(theta) * cosinePhi) + yAxis * (GetSine(theta) * cosinePhi) + zAxis * GetSine(phi);
    }

    private static double GetDistanceFromRightAngle(PrcVector from, PrcVector to) =>
        from.TryGetAngleTo(to, out var angle) ? Math.Abs(angle - RightAngle) : RightAngle;

    private double ReadAngle()
    {
        if (_angleCursor >= _data.NormalAngles.Length)
        {
            throw _bits.CreateCorruptException("a compressed tessellation runs out of normal angles before its triangles end");
        }

        return _data.NormalAngles[_angleCursor++] * _angleStep;
    }

    private bool ReadBit()
    {
        if (_bitCursor >= _data.NormalBinaryData.Length)
        {
            throw _bits.CreateCorruptException("a compressed tessellation runs out of normal data before its triangles end");
        }

        return _data.NormalBinaryData[_bitCursor++];
    }

    private static double GetSine(double angle) => angle > EighthTurn ? GetTaylorCosine(RightAngle - angle) : GetTaylorSine(angle);

    private static double GetCosine(double angle) => angle > EighthTurn ? GetTaylorSine(RightAngle - angle) : GetTaylorCosine(angle);

    private static double GetTaylorSine(double angle)
    {
        var square = angle * angle;
        return angle * (1.0 - square * (1.0 / 6.0 - square * (1.0 / 120.0 - square * (1.0 / 5040.0))));
    }

    private static double GetTaylorCosine(double angle)
    {
        var square = angle * angle;
        return 1.0 - square * (1.0 / 2.0 - square * (1.0 / 24.0 - square * (1.0 / 720.0)));
    }
}
