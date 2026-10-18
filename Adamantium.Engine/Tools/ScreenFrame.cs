using System;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// A frame drawn on an output in its pixels, as a frame selection takes it, CAD style: drawn to the right it takes
/// what lies wholly inside it; drawn to the left - <see cref="Crossing"/> - it takes whatever it touches.
/// </summary>
public readonly struct ScreenFrame
{
    public ScreenFrame(Vector2F from, Vector2F to)
    {
        Min = new Vector2F(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y));
        Max = new Vector2F(Math.Max(from.X, to.X), Math.Max(from.Y, to.Y));
        Crossing = to.X < from.X;
    }

    public Vector2F Min { get; }

    public Vector2F Max { get; }

    /// <summary>Whether the frame takes what it touches, not only what lies wholly inside.</summary>
    public bool Crossing { get; }

    public Vector2F Center => (Min + Max) / 2;

    public bool Contains(Vector2F pixel) =>
        pixel.X >= Min.X && pixel.X <= Max.X && pixel.Y >= Min.Y && pixel.Y <= Max.Y;

    /// <summary>Whether a segment on screen touches the frame: an end inside it, or the segment across an edge.</summary>
    public bool Touches(Vector2F a, Vector2F b)
    {
        if (Contains(a) || Contains(b))
        {
            return true;
        }

        var topRight = new Vector2F(Max.X, Min.Y);
        var bottomLeft = new Vector2F(Min.X, Max.Y);
        return Cross(a, b, Min, topRight) || Cross(a, b, topRight, Max) || Cross(a, b, Max, bottomLeft) || Cross(a, b, bottomLeft, Min);
    }

    /// <summary>Whether a triangle on screen touches the frame: overlapping it anywhere.</summary>
    public bool Touches(Vector2F a, Vector2F b, Vector2F c) =>
        Touches(a, b) || Touches(b, c) || Touches(c, a) || Inside(Min, a, b, c);

    /// <summary>Whether a box on screen is taken: wholly inside the frame, or, crossing, overlapping it.</summary>
    public bool Takes(Vector2F boxMin, Vector2F boxMax) => Crossing
        ? boxMin.X <= Max.X && boxMax.X >= Min.X && boxMin.Y <= Max.Y && boxMax.Y >= Min.Y
        : boxMin.X >= Min.X && boxMax.X <= Max.X && boxMin.Y >= Min.Y && boxMax.Y <= Max.Y;

    private static float Side(Vector2F a, Vector2F b, Vector2F p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static bool Cross(Vector2F a, Vector2F b, Vector2F c, Vector2F d)
    {
        var c1 = Side(a, b, c);
        var d1 = Side(a, b, d);
        var a2 = Side(c, d, a);
        var b2 = Side(c, d, b);
        if (c1 == 0 && d1 == 0)
        {
            return Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))
                   && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        }

        return c1 * d1 <= 0 && a2 * b2 <= 0;
    }

    private static bool Inside(Vector2F p, Vector2F a, Vector2F b, Vector2F c)
    {
        if (Side(a, b, c) == 0)
        {
            return false;
        }

        var ab = Side(a, b, p);
        var bc = Side(b, c, p);
        var ca = Side(c, a, p);
        return (ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0);
    }
}
