using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class ColorPaintTests
{
    private const double Tolerance = 0.005;
    private const double RelativeTolerance = 0.001;

    private static readonly ConcurrentDictionary<string, IFont> Fonts = new();

    public static IEnumerable<TestCaseData> ReferenceCases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "ColorPaint", "expected.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            yield return new TestCaseData(fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture), fields[2],
                    uint.Parse(fields[3], CultureInfo.InvariantCulture), fields[4].Replace(',', ' '), fields[5])
                .SetName($"{Path.GetFileName(fields[0])} palette {fields[1]} {fields[2]} glyph {fields[3]}");
        }
    }

    [TestCaseSource(nameof(ReferenceCases))]
    public void PaintsLikeHarfBuzz(string fontPath, int palette, string variations, uint glyphIndex, string clipBox,
        string expected)
    {
        var font = Fonts.GetOrAdd(fontPath + variations, _ => Instance(fontPath, variations));

        var actual = Format(font.GetColorPaint(glyphIndex, palette));
        if (font.TryGetColorClipBox(glyphIndex, out var box))
        {
            var actualBox = string.Join(" ", new[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height }
                .Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)));
            Assert.That(Matches(actualBox, clipBox), Is.True, $"\nclip box expected: {clipBox}\nactual:   {actualBox}");
        }

        Assert.That(font.TryGetColorClipBox(glyphIndex, out _) || clipBox != "-", Is.True,
            "HarfBuzz clips every glyph: to its clip box, or to what it paints when the list has none");
        Assert.That(Matches(actual, expected), Is.True, $"\nexpected: {expected}\nactual:   {actual}");
    }

    private static IFont Instance(string fontPath, string variations)
    {
        var font = Typeface.LoadFont(fontPath, 3).GetFont(0);
        var values = variations.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Split('='))
            .Select(v => new FontVariation(v[0], float.Parse(v[1], CultureInfo.InvariantCulture)))
            .ToArray();
        return values.Length == 0 ? font : font.GetInstance(values);
    }

    // Not among the reference cases: HarfBuzz 8.3 maps "no variation" through the font's delta-set index map to its last
    // entry, so with the font's last axis (CLIO) set it moves paints that have no variation index at all.
    [Test]
    public void AClipBox_VariesWithItsAxis_AndAPaintWithoutAVariationIndexDoesNot()
    {
        var font = Typeface.LoadFont("ColorFonts/test_glyphs-glyf_colr_1_variable.ttf", 3).GetFont(0);
        var inset = font.GetInstance([new FontVariation("CLIO", 40)]);

        Assert.That(inset.TryGetColorClipBox(166, out var box), Is.True);
        Assert.That(new[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height },
            Is.EqualTo(new[] { 140f, 140f, 860f, 860f }).Within(0.5f), "as HarfBuzz insets it, to whole units");
        Assert.That(Format(inset.GetColorPaint(8)), Is.EqualTo(Format(font.GetColorPaint(8))),
            "glyph 8's gradient has no variation index");
    }

    [TestCase(8u, true)]
    [TestCase(83u, true)]
    [TestCase(90u, true)]
    [TestCase(220u, true)]
    [TestCase(84u, false)]
    [TestCase(99u, false)]
    public void TheClipList_GivesTheGlyphsItCoversABox(uint glyph, bool covered)
    {
        var font = Fonts.GetOrAdd("ColorFonts/test_glyphs-glyf_colr_1.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(font.TryGetColorClipBox(glyph, out _), Is.EqualTo(covered), "its records cover 8-83, 90-98, 177-220");
    }

    [Test]
    public void ThePalettes_SayTheBackgroundsTheySuit()
    {
        var font = Fonts.GetOrAdd("ColorFonts/test_glyphs-glyf_colr_1.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(font.ColorPalettes.Select(p => p.Usage), Is.EqualTo(new[]
        {
            ColorPaletteUsage.None, ColorPaletteUsage.DarkBackground, ColorPaletteUsage.LightBackground
        }));
        Assert.That(font.ColorPalettes.Select(p => p.Index), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void APaletteTheFontLacks_DrawsInItsFirst()
    {
        var font = Fonts.GetOrAdd("ColorFonts/test_glyphs-glyf_colr_1.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));
        var glyph = FirstPainted(font);

        Assert.That(Format(font.GetColorPaint(glyph, 7)), Is.EqualTo(Format(font.GetColorPaint(glyph, 0))));
        Assert.That(Format(font.GetColorPaint(glyph, -1)), Is.EqualTo(Format(font.GetColorPaint(glyph, 0))));
    }

    private static uint FirstPainted(IFont font)
    {
        for (uint glyph = 0; glyph < font.Typeface.GlyphCount; glyph++)
        {
            if (font.GetColorPaint(glyph).Count > 0)
            {
                return glyph;
            }
        }

        throw new InvalidOperationException("the font has no paint graph");
    }

    [Test]
    public void AGlyphWithoutAPaintGraph_HasNoSteps()
    {
        var font = Fonts.GetOrAdd("TTFFonts/CascadiaCode-Regular.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(font.GetColorPaint(font.GetGlyphByUnicode('a').Index), Is.Empty);
    }

    private static string Format(IReadOnlyList<ColorPaintOperation> operations)
    {
        return string.Join(" | ", operations.Select(Format));
    }

    private static string Format(ColorPaintOperation operation)
    {
        switch (operation.Kind)
        {
            case ColorPaintOperationKind.PushClip:
                var clip = new StringBuilder("clip ").Append(operation.GlyphIndex);
                Append(clip, Values(operation.Transform));
                return clip.ToString();
            case ColorPaintOperationKind.PopClip:
                return "popclip";
            case ColorPaintOperationKind.PushGroup:
                return "group";
            case ColorPaintOperationKind.PopGroup:
                return $"popgroup {(int)operation.Mode}";
        }

        var fill = operation.Fill;
        var text = new StringBuilder("fill ");
        text.Append(Kind(fill.Kind)).Append(' ').Append(fill.Extend.ToString().ToLowerInvariant());
        Append(text, Values(fill.Transform));
        Append(text, fill.Kind switch
        {
            ColorFillKind.LinearGradient => [fill.Point0.X, fill.Point0.Y, fill.Point1.X, fill.Point1.Y, fill.Point2.X, fill.Point2.Y],
            ColorFillKind.RadialGradient => [fill.Point0.X, fill.Point0.Y, fill.Radius0, fill.Point1.X, fill.Point1.Y, fill.Radius1],
            ColorFillKind.SweepGradient => [fill.Point0.X, fill.Point0.Y, fill.StartAngle, fill.EndAngle],
            _ => [],
        });
        text.Append(' ').Append(fill.Stops.Count);
        foreach (var stop in fill.Stops)
        {
            var color = stop.Color is { } c ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : "fg";
            var alpha = stop.Color is { } a ? a.A / 255.0 : stop.Alpha;
            Append(text, [stop.Offset]);
            text.Append(' ').Append(color);
            Append(text, [alpha]);
        }

        return text.ToString();
    }

    private static double[] Values(Matrix3x2<double> m) => [m.M11, m.M12, m.M21, m.M22, m.M31, m.M32];

    private static string Kind(ColorFillKind kind) =>
        kind switch
        {
            ColorFillKind.LinearGradient => "linear",
            ColorFillKind.RadialGradient => "radial",
            ColorFillKind.SweepGradient => "sweep",
            _ => "solid",
        };

    private static void Append(StringBuilder text, double[] values)
    {
        foreach (var value in values)
        {
            text.Append(' ').Append(Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture));
        }
    }

    private static bool Matches(string actual, string expected)
    {
        var a = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var e = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length != e.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            var isNumber = double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
            if (isNumber && double.TryParse(e[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                if (Math.Abs(x - y) > Tolerance + RelativeTolerance * Math.Abs(y))
                {
                    return false;
                }
            }
            else if (a[i] != e[i])
            {
                return false;
            }
        }

        return true;
    }
}
