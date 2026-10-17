using System;
using System.Linq;
using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Parsers;

internal sealed class VariedTTFGlyphOutlineSource : IGlyphOutlineSource
{
    private const int PhantomCount = 4;

    private readonly SfntParser parser;
    private readonly byte[] fontData;
    private readonly long glyfTableOffset;
    private readonly uint[] glyphOffsets;
    private readonly Typeface typeface;
    private readonly Font font;
    private readonly float[] coordinates;
    private readonly double[] originShifts;

    public VariedTTFGlyphOutlineSource(SfntParser parser, byte[] fontData, long glyfTableOffset, uint[] glyphOffsets,
        int glyphCount, Typeface typeface, Font font, float[] coordinates)
    {
        this.parser = parser;
        this.fontData = fontData;
        this.glyfTableOffset = glyfTableOffset;
        this.glyphOffsets = glyphOffsets;
        this.typeface = typeface;
        this.font = font;
        this.coordinates = coordinates;
        originShifts = new double[glyphCount];
    }

    public void LoadOutlines(Glyph glyph)
    {
        var index = glyph.Index;
        var offset = glyphOffsets[index];
        if (offset == glyphOffsets[index + 1] || offset >= glyphOffsets[glyphOffsets.Length - 1])
        {
            var xs = new double[PhantomCount];
            var ys = new double[PhantomCount];
            SetPhantoms(index, 0, xs, 0);
            font.GlyphVariations.Apply(index, coordinates, xs, ys, null);
            Finish(glyph, xs, 0);
            return;
        }

        using var reader = new FontStreamReader(fontData);
        reader.Position = glyfTableOffset + offset;
        var header = parser.ReadGlyphData(reader, glyph);
        if (header.NumberOfContours >= 0)
        {
            VarySimple(glyph, header.XMin);
        }
        else
        {
            VaryComposite(glyph, header.XMin);
        }
    }

    private void VarySimple(Glyph glyph, short xMin)
    {
        var outlines = glyph.Outlines.ToArray();
        var count = outlines.Sum(o => o.Points.Count);
        var xs = new double[count + PhantomCount];
        var ys = new double[count + PhantomCount];
        var contourEnds = new int[outlines.Length];
        var k = 0;
        for (var c = 0; c < outlines.Length; c++)
        {
            foreach (var point in outlines[c].Points)
            {
                xs[k] = point.X;
                ys[k] = point.Y;
                k++;
            }

            contourEnds[c] = k - 1;
        }

        SetPhantoms(glyph.Index, xMin, xs, count);
        font.GlyphVariations.Apply(glyph.Index, coordinates, xs, ys, contourEnds);

        var shift = xs[count];
        k = 0;
        foreach (var outline in outlines)
        {
            for (var i = 0; i < outline.Points.Count; i++, k++)
            {
                outline.Points[i] = new OutlinePoint(xs[k] - shift, ys[k], outline.Points[i].IsControl);
            }
        }

        Finish(glyph, xs, count);
    }

    private void VaryComposite(Glyph glyph, short xMin)
    {
        var components = glyph.CompositeGlyphComponents;
        var count = components.Count;
        var xs = new double[count + PhantomCount];
        var ys = new double[count + PhantomCount];
        for (var i = 0; i < count; i++)
        {
            xs[i] = components[i].TransformMatrix.M31;
            ys[i] = components[i].TransformMatrix.M32;
        }

        SetPhantoms(glyph.Index, xMin, xs, count);
        font.GlyphVariations.Apply(glyph.Index, coordinates, xs, ys, null);
        foreach (var component in components)
        {
            if (component.UseMyMetrics)
            {
                var metrics = GlyphAt(component.SimpleGlyphIndex);
                _ = metrics.HasOutlines;
                xs[count] = originShifts[component.SimpleGlyphIndex];
                xs[count + 1] = xs[count] + metrics.AdvanceWidth;
            }
        }

        var shift = xs[count];
        for (var i = 0; i < count; i++)
        {
            var component = components[i];
            _ = GlyphAt(component.SimpleGlyphIndex).HasOutlines;
            var componentShift = originShifts[component.SimpleGlyphIndex];
            var matrix = component.TransformMatrix;
            matrix.M31 = xs[i] + componentShift * matrix.M11 - shift;
            matrix.M32 = ys[i] + componentShift * matrix.M12;
            component.TransformMatrix = matrix;
        }

        glyph.AddComponentOutlines(GlyphAt);
        Finish(glyph, xs, count);
    }

    private Glyph GlyphAt(uint index)
    {
        typeface.GetGlyphByIndex(index, out var glyph);
        return glyph;
    }

    private void SetPhantoms(uint glyphIndex, short xMin, double[] xs, int count)
    {
        xs[count] = xMin - font.GetLeftSideBearing(glyphIndex);
        xs[count + 1] = xs[count] + font.GetAdvanceWidth(glyphIndex);
    }

    private void Finish(Glyph glyph, double[] xs, int count)
    {
        originShifts[glyph.Index] = xs[count];
        glyph.AdvanceWidth = (ushort)Math.Max(0, Math.Floor(xs[count + 1] - xs[count] + 0.5));

        glyph.RecalculateBounds(true);
        glyph.LeftSideBearing = (short)glyph.BoundingRectangle.X;
    }
}
