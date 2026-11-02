using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>
/// A shape text is set inside (<see cref="TextLayout.Shape"/>) or flows round (<see cref="TextLayout.ExclusionShapes"/>),
/// in the layout's units: closed <see cref="Contours"/>, filled even-odd - a ring's hole is outside it, and contours may
/// cross - kept <see cref="Padding"/> away from. A row of text takes the stretches across that are inside the shape all
/// the way down the row, or that nothing of the shape reaches. The contours are not to change once given; a shape is
/// asked of one layout at a time.
/// </summary>
public sealed class TextShape
{
    private readonly List<double> _crossings = [];
    private readonly List<(double Left, double Right)> _slice = [];
    private readonly List<(double Left, double Right)> _edges = [];
    private readonly List<(double Left, double Right)> _kept = [];

    /// <summary>A shape of <paramref name="contours"/>, each closed from its last point to its first.</summary>
    public TextShape(IReadOnlyList<Vector2<double>[]> contours, double padding = 0)
    {
        Contours = contours ?? throw new ArgumentNullException(nameof(contours));
        Padding = Math.Max(0, padding);
    }

    /// <summary>The closed outlines of the shape.</summary>
    public IReadOnlyList<Vector2<double>[]> Contours { get; }

    /// <summary>How far text keeps from the shape's edge: inside it, from the edge in; round it, from the edge out.</summary>
    public double Padding { get; }

    /// <summary>Puts into <paramref name="spans"/>, left to right, the stretches across that are inside the shape - and
    /// <see cref="Padding"/> in from its edge - everywhere from <paramref name="top"/> down to
    /// <paramref name="bottom"/>.</summary>
    public void Inside(double top, double bottom, List<(double Left, double Right)> spans)
    {
        Read(top - Padding, bottom + Padding);
        spans.Clear();
        foreach (var (left, right) in _slice)
        {
            var from = left;
            foreach (var (edgeLeft, edgeRight) in _edges)
            {
                if (edgeRight <= from || edgeLeft >= right)
                {
                    continue;
                }

                Keep(spans, from + Padding, edgeLeft - Padding);
                from = Math.Max(from, edgeRight);
            }

            Keep(spans, from + Padding, right - Padding);
        }
    }

    /// <summary>Puts into <paramref name="spans"/>, left to right and apart, the stretches across that the shape - and
    /// <see cref="Padding"/> round it - reaches anywhere from <paramref name="top"/> down to
    /// <paramref name="bottom"/>.</summary>
    public void Covered(double top, double bottom, List<(double Left, double Right)> spans)
    {
        Read(top - Padding, bottom + Padding);
        spans.Clear();
        foreach (var (left, right) in _slice)
        {
            spans.Add((left - Padding, right + Padding));
        }

        foreach (var (left, right) in _edges)
        {
            spans.Add((left - Padding, right + Padding));
        }

        Merge(spans);
    }

    /// <summary>The highest and lowest of the shape's points; false for a shape of none.</summary>
    public bool TryGetBounds(out Vector2<double> min, out Vector2<double> max)
    {
        min = new Vector2<double>(double.PositiveInfinity, double.PositiveInfinity);
        max = new Vector2<double>(double.NegativeInfinity, double.NegativeInfinity);
        foreach (var contour in Contours)
        {
            foreach (var point in contour)
            {
                min = Vector2<double>.Min(min, point);
                max = Vector2<double>.Max(max, point);
            }
        }

        return max.X >= min.X;
    }

    private void Read(double top, double bottom)
    {
        _slice.Clear();
        _edges.Clear();
        _crossings.Clear();
        var middle = (top + bottom) / 2;
        foreach (var contour in Contours)
        {
            for (int i = 0, j = contour.Length - 1; i < contour.Length; j = i++)
            {
                var (a, b) = (contour[j], contour[i]);
                if (a.Y <= middle != b.Y <= middle)
                {
                    _crossings.Add(XAt(a, b, middle));
                }

                var (low, high) = a.Y < b.Y ? (a, b) : (b, a);
                if (high.Y <= top || low.Y >= bottom)
                {
                    continue;
                }

                var from = low.Y < top ? XAt(low, high, top) : low.X;
                var to = high.Y > bottom ? XAt(low, high, bottom) : high.X;
                _edges.Add((Math.Min(from, to), Math.Max(from, to)));
            }
        }

        _crossings.Sort();
        for (var i = 0; i + 1 < _crossings.Count; i += 2)
        {
            if (_crossings[i + 1] > _crossings[i])
            {
                _slice.Add((_crossings[i], _crossings[i + 1]));
            }
        }

        Merge(_edges);
    }

    private static double XAt(Vector2<double> a, Vector2<double> b, double y) =>
        a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);

    private static void Keep(List<(double Left, double Right)> spans, double left, double right)
    {
        if (right > left)
        {
            spans.Add((left, right));
        }
    }

    private void Merge(List<(double Left, double Right)> spans)
    {
        spans.Sort((x, y) => x.Left.CompareTo(y.Left));
        _kept.Clear();
        foreach (var span in spans)
        {
            if (_kept.Count > 0 && span.Left <= _kept[^1].Right)
            {
                _kept[^1] = (_kept[^1].Left, Math.Max(_kept[^1].Right, span.Right));
            }
            else
            {
                _kept.Add(span);
            }
        }

        spans.Clear();
        spans.AddRange(_kept);
    }
}
