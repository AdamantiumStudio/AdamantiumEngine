using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the normals of a highly compressed tessellation as its triangles are built (Mesh Normal
/// Description, Mesh Normal Construction). Each vertex keeps the normals stored for it. Met for the first time, it
/// says whether it has several, then stores one; met again, one of a single normal keeps it, and one of several says
/// whether it refers to one it stored - by how many back from the newest, in as many bits as the newest's index
/// needs - or stores another. A planar face has one normal, read so for the first vertex of its first triangle. A
/// stored normal is three flags and two angles: the flags reverse the z, x and y axes of a system built on the
/// triangle's corner nearest a right angle, and the angles, steps of a right angle over the largest value of their
/// width, are its spherical coordinates there - their sines and cosines from a Taylor series of four terms.</summary>
internal sealed class PrcCompressedNormals
{
    private const double RightAngle = Math.PI / 2;
    private const double EighthTurn = Math.PI / 4;
    private const int LongestAngle = 16;

    private readonly PrcCompressedTessellationData data;
    private readonly PrcBitReader bits;
    private readonly Dictionary<int, PrcNormalSlot> vertexSlots = new();
    private readonly Dictionary<int, int> faceNormals = new();
    private readonly double angleStep;
    private int bitCursor;
    private int angleCursor;

    internal PrcCompressedNormals(PrcCompressedTessellationData data, PrcBitReader bits)
    {
        this.data = data;
        this.bits = bits;
        if (data.NormalAngleBits < 1 || data.NormalAngleBits > LongestAngle)
        {
            throw bits.Corrupt($"a compressed tessellation stores its normal angles in {data.NormalAngleBits} bits");
        }

        angleStep = RightAngle / ((1 << data.NormalAngleBits) - 1);
    }

    /// <summary>Every normal stored, in order.</summary>
    internal List<PrcVector> All { get; } = [];

    /// <summary>Whether every flag and angle stored has been read.</summary>
    internal bool IsAtEnd => bitCursor == data.NormalBinaryData.Length && angleCursor == data.NormalAngles.Length;

    internal PrcVector NormalAt(int index) => All[index];

    /// <summary>Reads the normals of <paramref name="triangle"/>, whose vertices are placed, into its normal indices.</summary>
    internal void Restore(PrcDecodedTriangle triangle, IReadOnlyList<PrcVector> points)
    {
        var first = points[triangle.First];
        var second = points[triangle.Second];
        var third = points[triangle.Third];
        if (triangle.Face < data.IsFacePlanar.Length && data.IsFacePlanar[triangle.Face])
        {
            if (!faceNormals.TryGetValue(triangle.Face, out var shared))
            {
                shared = Restore(SlotOf(triangle.First), first, second, third);
                faceNormals.Add(triangle.Face, shared);
            }

            triangle.FirstNormal = shared;
            triangle.SecondNormal = shared;
            triangle.ThirdNormal = shared;
            return;
        }

        triangle.FirstNormal = Restore(SlotOf(triangle.First), first, second, third);
        triangle.SecondNormal = Restore(SlotOf(triangle.Second), first, second, third);
        triangle.ThirdNormal = Restore(SlotOf(triangle.Third), first, second, third);
    }

    private PrcNormalSlot SlotOf(int vertex)
    {
        if (!vertexSlots.TryGetValue(vertex, out var slot))
        {
            slot = new PrcNormalSlot();
            vertexSlots.Add(vertex, slot);
        }

        return slot;
    }

    private int Restore(PrcNormalSlot slot, PrcVector first, PrcVector second, PrcVector third)
    {
        if (!slot.IsMet)
        {
            slot.IsMet = true;
            slot.HasSeveral = NextBit();
        }
        else if (!slot.HasSeveral)
        {
            return slot.Normals[0];
        }
        else if (NextBit())
        {
            var newest = slot.Normals.Count - 1;
            var back = 0;
            for (var bit = 0; bit < PrcBitWidths.UnsignedInteger(newest); bit++)
            {
                if (NextBit())
                {
                    back |= 1 << bit;
                }
            }

            if (back > newest)
            {
                throw bits.Corrupt($"a normal of a compressed tessellation refers {back} back of the {slot.Normals.Count} its vertex stored");
            }

            return slot.Normals[newest - back];
        }

        All.Add(NextNormal(first, second, third));
        slot.Normals.Add(All.Count - 1);
        return All.Count - 1;
    }

    private PrcVector NextNormal(PrcVector first, PrcVector second, PrcVector third)
    {
        var reverseZ = NextBit();
        var reverseX = NextBit();
        var reverseY = NextBit();
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

        if (!zAxis.TryUnitize(out zAxis))
        {
            xAxis.TryOrthogonal(out zAxis);
        }

        if (reverseZ)
        {
            zAxis = -zAxis;
        }

        zAxis.Cross(xAxis).TryUnitize(out var yAxis);
        xAxis.TryUnitize(out xAxis);
        if (reverseX)
        {
            xAxis = -xAxis;
        }

        if (reverseY)
        {
            yAxis = -yAxis;
        }

        var theta = NextAngle();
        var phi = NextAngle();
        var cosinePhi = Cosine(phi);
        return xAxis * (Cosine(theta) * cosinePhi) + yAxis * (Sine(theta) * cosinePhi) + zAxis * Sine(phi);
    }

    private static double OffRightAngle(PrcVector from, PrcVector to) =>
        from.TryAngleTo(to, out var angle) ? Math.Abs(angle - RightAngle) : RightAngle;

    private double NextAngle()
    {
        if (angleCursor >= data.NormalAngles.Length)
        {
            throw bits.Corrupt("a compressed tessellation runs out of normal angles before its triangles end");
        }

        return data.NormalAngles[angleCursor++] * angleStep;
    }

    private bool NextBit()
    {
        if (bitCursor >= data.NormalBinaryData.Length)
        {
            throw bits.Corrupt("a compressed tessellation runs out of normal data before its triangles end");
        }

        return data.NormalBinaryData[bitCursor++];
    }

    private static double Sine(double angle) => angle > EighthTurn ? TaylorCosine(RightAngle - angle) : TaylorSine(angle);

    private static double Cosine(double angle) => angle > EighthTurn ? TaylorSine(RightAngle - angle) : TaylorCosine(angle);

    private static double TaylorSine(double angle)
    {
        var square = angle * angle;
        return angle * (1.0 - square * (1.0 / 6.0 - square * (1.0 / 120.0 - square * (1.0 / 5040.0))));
    }

    private static double TaylorCosine(double angle)
    {
        var square = angle * angle;
        return 1.0 - square * (1.0 / 2.0 - square * (1.0 / 24.0 - square * (1.0 / 720.0)));
    }
}

/// <summary>The normals stored for one vertex: whether it has been met, whether it has several, and the indices of
/// those stored.</summary>
internal sealed class PrcNormalSlot
{
    internal bool IsMet { get; set; }

    internal bool HasSeveral { get; set; }

    internal List<int> Normals { get; } = [];
}

/// <summary>The number of bits the specification stores an unsigned value in (GetNumberOfBitsUsedToStoreUnsignedInteger):
/// one, and one more for each doubling past one that the value needs.</summary>
internal static class PrcBitWidths
{
    internal static int UnsignedInteger(int value)
    {
        var width = 1;
        for (var limit = 1L; value > limit; limit *= 2)
        {
            width++;
        }

        return width;
    }
}
