using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>Measures of a closed ring, its last point joined to its first.</summary>
internal static class RingMath
{
    /// <summary>The area <paramref name="ring"/> bounds: positive when it turns anticlockwise, y up. Measured from the
    /// ring's first point, so a ring far from the origin keeps the digits of its own size.</summary>
    public static double SignedArea(IReadOnlyList<Vector2<double>> ring)
    {
        if (ring.Count < 3)
        {
            return 0;
        }

        var origin = ring[0];
        var sum = 0.0;
        for (var i = 1; i + 1 < ring.Count; i++)
        {
            var (a, b) = (ring[i] - origin, ring[i + 1] - origin);
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2;
    }

    /// <summary>The power of two, as its exponent, that brings coordinates as large as <paramref name="largest"/> near
    /// one when squaring them would leave the doubles - or 0. Scaling by it (<see cref="Math.ScaleB"/>) and back loses no
    /// digit.</summary>
    public static int Scale(double largest)
    {
        if (largest is > 0 and (> 1e100 or < 1e-100) && double.IsFinite(largest))
        {
            return -Math.ILogB(largest);
        }

        return 0;
    }

    /// <summary><paramref name="p"/> times two to <paramref name="exponent"/>.</summary>
    public static Vector2<double> Scaled(Vector2<double> p, int exponent) =>
        new(Math.ScaleB(p.X, exponent), Math.ScaleB(p.Y, exponent));

    /// <summary><paramref name="p"/> times two to <paramref name="exponent"/>.</summary>
    public static Vector3<double> Scaled(Vector3<double> p, int exponent) =>
        new(Math.ScaleB(p.X, exponent), Math.ScaleB(p.Y, exponent), Math.ScaleB(p.Z, exponent));

    /// <summary>The largest coordinate, as a magnitude, of the finite rings among <paramref name="rings"/>.</summary>
    public static double Largest(IReadOnlyList<Vector2<double>[]> rings)
    {
        var largest = 0.0;
        foreach (var ring in rings)
        {
            if (ring == null || !Finite(ring)) continue;
            foreach (var p in ring) largest = Math.Max(largest, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
        }

        return largest;
    }

    /// <summary><paramref name="rings"/> with every coordinate times two to <paramref name="exponent"/>, or the rings
    /// themselves when it is 0.</summary>
    public static IReadOnlyList<Vector2<double>[]> Scaled(IReadOnlyList<Vector2<double>[]> rings, int exponent)
    {
        if (exponent == 0) return rings;
        var scaled = new Vector2<double>[rings.Count][];
        for (var r = 0; r < rings.Count; r++)
        {
            var ring = rings[r];
            if (ring == null) continue;
            scaled[r] = new Vector2<double>[ring.Length];
            for (var i = 0; i < ring.Length; i++) scaled[r][i] = Scaled(ring[i], exponent);
        }

        return scaled;
    }

    /// <summary>Whether every coordinate of <paramref name="ring"/> is a finite number.</summary>
    public static bool Finite(IReadOnlyList<Vector2<double>> ring)
    {
        foreach (var p in ring)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            {
                return false;
            }
        }

        return true;
    }
}
