using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class MsdfFieldLeakTests
{
    private const uint FieldSize = 64;
    private const byte PixelRange = 16;
    private const uint Margin = 8;
    private const double ScreenRange = 18;

    // Glyphs whose fields drew a line past an acute corner, along an edge's extension, out to the cell's edge.
    [TestCase(441u)]
    [TestCase(848u)]
    [TestCase(849u)]
    [TestCase(858u)]
    [TestCase(868u)]
    [TestCase(881u)]
    [TestCase(900u)]
    [TestCase(926u)]
    [TestCase(954u)]
    [TestCase(978u)]
    [TestCase(1005u)]
    [TestCase(1018u)]
    public void AFieldDrawsNothingAwayFromTheOutline(uint glyphIndex)
    {
        var font = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).GetFont(0);
        var glyph = font.GetGlyphByIndex(glyphIndex);
        glyph.CalculateEmRelatedMultipliers(font.UnitsPerEm);
        glyph.Sample(5);
        var field = glyph.GenerateDirectMSDF(FieldSize, PixelRange, font.UnitsPerEm, Margin);
        var segments = glyph.GetMergedOutlineSegments();
        var bounds = glyph.BoundingRectangle;
        var width = (int)field.FullGlyphSize.Width;
        var height = (int)field.FullGlyphSize.Height;
        var texelX = (double)bounds.Width / (width - 2 * Margin);
        var texelY = (double)bounds.Height / (height - 2 * Margin);
        var drawn = new List<string>();

        for (var y = 0; y < height - 1; y++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                var point = new Vector2<double>(texelX * (x + 1 - Margin) + bounds.X,
                    bounds.Height - (texelY * (y + 1 - Margin) - bounds.Y));
                if (Inside(segments, point) || Distance(segments, point) < 1.5 * Math.Max(texelX, texelY))
                {
                    continue;
                }

                var median = Median(Between(field, width, x, y, 0), Between(field, width, x, y, 1), Between(field, width, x, y, 2));
                var coverage = Math.Clamp(ScreenRange * (median - 0.5) + 0.5, 0, 1);
                if (coverage > 0.1)
                {
                    drawn.Add($"({x + 1},{y + 1}) {coverage:0.00}");
                }
            }
        }

        Assert.That(drawn, Is.Empty, "points well outside the outline the field draws");
    }

    // 0.8 texels out from the lower left corner of 'L' along its diagonal: inside the corner moved out by a texel, as a
    // synthesized bold moves it, while it is sharp; outside it once rounded.
    [Test]
    public void ACornerStaysSharp_ForAContourMovedOutByATexel()
    {
        var font = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).GetFont(0);
        var glyph = font.GetGlyphByUnicode('L');
        glyph.CalculateEmRelatedMultipliers(font.UnitsPerEm);
        glyph.Sample(5);
        var field = glyph.GenerateDirectMSDF(FieldSize, PixelRange, font.UnitsPerEm, Margin);
        var bounds = glyph.BoundingRectangle;
        var width = (int)field.FullGlyphSize.Width;
        var height = (int)field.FullGlyphSize.Height;
        var texelY = (double)bounds.Height / (height - 2 * Margin);

        var x = -0.8 + Margin - 0.5;
        var y = bounds.Height / texelY + 0.8 + Margin - 0.5;
        var median = Median(Bilinear(field, width, x, y, 0), Bilinear(field, width, x, y, 1), Bilinear(field, width, x, y, 2));

        Assert.That(median, Is.GreaterThan(0.5 - 1.0 / PixelRange));
    }

    private static double Bilinear(GlyphTextureData field, int width, double x, double y, int channel)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var u = x - x0;
        var v = y - y0;
        double Texel(int tx, int ty) => field.Pixels[(ty * width + tx) * 4 + channel] / 255.0;
        return Texel(x0, y0) * (1 - u) * (1 - v) + Texel(x0 + 1, y0) * u * (1 - v) + Texel(x0, y0 + 1) * (1 - u) * v +
               Texel(x0 + 1, y0 + 1) * u * v;
    }

    private static double Between(GlyphTextureData field, int width, int x, int y, int channel)
    {
        double Texel(int tx, int ty) => field.Pixels[(ty * width + tx) * 4 + channel] / 255.0;
        return (Texel(x, y) + Texel(x + 1, y) + Texel(x, y + 1) + Texel(x + 1, y + 1)) / 4;
    }

    private static double Median(double r, double g, double b) => Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b));

    private static bool Inside(List<LineSegment2D> segments, Vector2<double> p)
    {
        var winding = 0;
        foreach (var segment in segments)
        {
            var a = segment.Start;
            var b = segment.End;
            var side = (b.X - a.X) * (p.Y - a.Y) - (p.X - a.X) * (b.Y - a.Y);
            if (a.Y <= p.Y && b.Y > p.Y && side > 0)
            {
                winding++;
            }
            else if (a.Y > p.Y && b.Y <= p.Y && side < 0)
            {
                winding--;
            }
        }

        return winding != 0;
    }

    private static double Distance(List<LineSegment2D> segments, Vector2<double> p)
    {
        var nearest = double.MaxValue;
        foreach (var segment in segments)
        {
            var a = segment.Start;
            var d = segment.End - a;
            var length = d.X * d.X + d.Y * d.Y;
            var t = length > 0 ? Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / length, 0, 1) : 0;
            var q = a + d * t;
            nearest = Math.Min(nearest, Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)));
        }

        return nearest;
    }
}
