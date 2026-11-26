using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Subsetting;

/// <summary>The widths and boxes of the glyphs an <see cref="OpenTypeSubset"/> writes, and its font's lines, at the
/// subset's units.</summary>
internal sealed class OpenTypeSubsetMetrics
{
    public OpenTypeSubsetMetrics(IFont font, IReadOnlyList<uint> indices)
    {
        Font = font;
        Scale = (double)CffSubset.UnitsPerEm / font.UnitsPerEm;
        Advances = new ushort[indices.Count];
        Bearings = new short[indices.Count];
        short xMin = short.MaxValue, yMin = short.MaxValue, xMax = short.MinValue, yMax = short.MinValue;
        var minRight = int.MaxValue;
        for (var i = 0; i < indices.Count; i++)
        {
            var glyph = font.GetGlyphByIndex(indices[i]);
            Advances[i] = (ushort)Math.Clamp(Math.Round(font.GetAdvanceWidth(indices[i]) * Scale), 0, ushort.MaxValue);
            var box = glyph.BoundingRectangle;
            if (!glyph.HasOutlines || !(box.Width > 0) || !(box.Height > 0))
            {
                continue;
            }

            var left = Em(box.X);
            var right = Em(box.X + box.Width);
            Bearings[i] = left;
            xMin = Math.Min(xMin, left);
            yMin = Math.Min(yMin, Em(box.Y));
            xMax = Math.Max(xMax, right);
            yMax = Math.Max(yMax, Em(box.Y + box.Height));
            minRight = Math.Min(minRight, Advances[i] - right);
            MaxExtent = Math.Max(MaxExtent, right);
            MinLeft = Math.Min(MinLeft, left);
            Inked = true;
        }

        if (Inked)
        {
            (XMin, YMin, XMax, YMax, MinRight) = (xMin, yMin, xMax, yMax, minRight);
        }
        else
        {
            MinLeft = 0;
        }

        // A font without 'OS/2' has the line's alone.
        var typo = font.Ascender != 0 || font.Descender != 0;
        Ascender = Em(typo ? font.Ascender : font.LineAscent);
        Descender = Em(typo ? font.Descender : -font.LineDescent);
        LineGap = Em(font.LineGap);
    }

    public IFont Font { get; }

    public double Scale { get; }

    public ushort[] Advances { get; }

    public short[] Bearings { get; }

    public bool Inked { get; }

    public short XMin { get; }

    public short YMin { get; }

    public short XMax { get; }

    public short YMax { get; }

    public int MinRight { get; }

    public short MinLeft { get; } = short.MaxValue;

    public short MaxExtent { get; }

    public short Ascender { get; }

    public short Descender { get; }

    public short LineGap { get; }

    /// <summary><paramref name="units"/> of the font at the subset's units, whole.</summary>
    public short Em(double units) => (short)Math.Clamp(Math.Round(units * Scale), short.MinValue, short.MaxValue);
}
