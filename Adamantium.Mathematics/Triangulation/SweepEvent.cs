using System;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>A segment joining or leaving a swept line at a point; ordered by x, then y, a leaving one first.</summary>
internal readonly struct SweepEvent : IComparable<SweepEvent>
{
    public SweepEvent(Vector2<double> at, bool leaves, int segment)
    {
        At = at;
        Leaves = leaves;
        Segment = segment;
    }

    public Vector2<double> At { get; }

    public bool Leaves { get; }

    public int Segment { get; }

    public int CompareTo(SweepEvent other)
    {
        if (At.X != other.At.X) return At.X < other.At.X ? -1 : 1;
        if (At.Y != other.At.Y) return At.Y < other.At.Y ? -1 : 1;
        return other.Leaves.CompareTo(Leaves);
    }
}
