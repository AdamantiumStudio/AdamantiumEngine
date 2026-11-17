using System;
using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Text;

/// <summary>How high a line being gathered reaches, across its baseline from its first glyph's origin, and its biggest
/// glyph: what tells whether the next glyph shares its height.</summary>
internal sealed class PdfLineHeight
{
    private const double RaisedDrift = 0.8;

    private double _low;
    private double _high;
    private double _size;

    public PdfLineHeight()
    {
        Clear();
    }

    public void Clear()
    {
        _low = double.MaxValue;
        _high = double.MinValue;
        _size = 0;
    }

    /// <summary>Takes in <paramref name="glyph"/>, with the line running along (<paramref name="dx"/>,
    /// <paramref name="dy"/>) from <paramref name="origin"/>.</summary>
    public void Widen(PdfTextGlyph glyph, PdfPoint origin, double dx, double dy)
    {
        Measure(glyph, origin, dx, dy, out var low, out var high);
        _low = Math.Min(_low, low);
        _high = Math.Max(_high, high);
        _size = Math.Max(_size, glyph.Size);
    }

    /// <summary>Whether <paramref name="glyph"/>'s baseline lies within the line's height, or its own height holds the
    /// line's baseline - a lowered glyph, or the text after a raised mark; a bigger glyph only close to the baseline.
    /// </summary>
    public bool Shares(PdfTextGlyph glyph, PdfPoint origin, double dx, double dy)
    {
        Measure(glyph, origin, dx, dy, out var low, out var high);
        if (_high - _low < 1e-9 || high - low < 1e-9)
        {
            return true;
        }

        var baseline = (glyph.Origin.X - origin.X) * -dy + (glyph.Origin.Y - origin.Y) * dx;
        var withinLine = baseline >= _low - 1e-9 && baseline <= _high + 1e-9;
        var nearLine = glyph.Size <= _size + 1e-9 || Math.Abs(baseline) <= RaisedDrift * _size;
        var holdsLine = nearLine && low <= 1e-9 && high >= -1e-9;
        return withinLine || holdsLine;
    }

    private static void Measure(PdfTextGlyph glyph, PdfPoint origin, double dx, double dy, out double low, out double high)
    {
        var quad = glyph.Quad;
        low = double.MaxValue;
        high = double.MinValue;
        foreach (var corner in (ReadOnlySpan<PdfPoint>)[quad.BottomLeft, quad.BottomRight, quad.TopRight, quad.TopLeft])
        {
            var height = (corner.X - origin.X) * -dy + (corner.Y - origin.Y) * dx;
            low = Math.Min(low, height);
            high = Math.Max(high, height);
        }
    }
}
