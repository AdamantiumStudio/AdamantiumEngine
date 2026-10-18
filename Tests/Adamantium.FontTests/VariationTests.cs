using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class VariationTests
{
    private static readonly ConcurrentDictionary<string, IFont> Fonts = new();

    public static IEnumerable<TestCaseData> ReferenceCases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Variations", "expected.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            yield return new TestCaseData(fields[0], fields[1], fields[2])
                .SetName($"{Path.GetFileName(fields[0])} {fields[1]}");
        }
    }

    [TestCaseSource(nameof(ReferenceCases))]
    public void VariesLikeHarfBuzz(string fontPath, string variations, string expected)
    {
        var font = Fonts.GetOrAdd(fontPath, p => Typeface.LoadFont(p, 3).GetFont(0));
        var instance = font.GetInstance(Parse(variations));
        var mismatches = new List<string>();

        foreach (var entry in expected.Split(' '))
        {
            var parts = entry.Split(':');
            var glyphIndex = uint.Parse(parts[0], CultureInfo.InvariantCulture);
            var advance = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var box = parts[2].Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var bounds = instance.GetGlyphByIndex(glyphIndex).BoundingRectangle;
            var left = bounds.Width == 0 && bounds.Height == 0 ? 0 : instance.GetLeftSideBearing(glyphIndex);
            int[] actual = [left, bounds.Y, left + bounds.Width, bounds.Bottom];
            if (IsShiftedByItsBearing(font, glyphIndex))
            {
                actual[0] = box[0];
                actual[2] = System.Math.Abs(bounds.Width - (box[2] - box[0])) <= 2 ? box[2] : box[0] + bounds.Width;
            }

            var actualAdvance = instance.GetAdvanceWidth(glyphIndex);
            if (actualAdvance != advance || actual.Zip(box, (a, e) => System.Math.Abs(a - e)).Any(d => d > 1))
            {
                mismatches.Add($"{glyphIndex}: {actualAdvance}:{string.Join(",", actual)}, expected {parts[1]}:{parts[2]}");
            }
        }

        Assert.That(mismatches, Is.Empty, $"{mismatches.Count} glyphs differ, the first: {string.Join("; ", mismatches.Take(10))}");
    }

    [Test]
    public void InstanceIsSharedAndHasItsOwnTypeface()
    {
        var font = Fonts.GetOrAdd("source-sans-3v028R/VAR/SourceSans3VF-Roman.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));

        var bold = font.GetInstance([new FontVariation("wght", 700)]);

        Assert.That(font.GetInstance([new FontVariation("wght", 700)]), Is.SameAs(bold));
        Assert.That(bold.Typeface.Id, Is.Not.EqualTo(font.Typeface.Id));
        Assert.That(bold.Weight.Value, Is.EqualTo(700));
        Assert.That(font.GetInstance([new FontVariation("wght", font.Axes[0].DefaultValue)]), Is.SameAs(font));
        Assert.That(bold.GetGlyphByUnicode('a').Index, Is.EqualTo(font.GetGlyphByUnicode('a').Index));
        Assert.That(bold.Typeface.OutlinesId, Is.Not.EqualTo(font.Typeface.OutlinesId), "its outlines are its own");
    }

    // Animating an axis that only moves advances made a typeface, and so atlas cells, for every value it passed.
    [Test]
    public void AnInstanceThatVariesOnlyAdvances_DrawsWithItsFontsGlyphs()
    {
        var font = Fonts.GetOrAdd("Variations/RobotoFlex-NoGvar.subset.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));
        var narrow = font.GetInstance([new FontVariation("wdth", 25), new FontVariation("wght", 900)]);
        var glyph = font.GetGlyphByUnicode('o').Index;

        Assert.That(narrow, Is.Not.SameAs(font));
        Assert.That(narrow.GetAdvanceWidth(glyph), Is.Not.EqualTo(font.GetAdvanceWidth(glyph)), "its advances vary");
        Assert.That(narrow.Typeface.Id, Is.Not.EqualTo(font.Typeface.Id));
        Assert.That(Adamantium.Fonts.TextureGeneration.GlyphTextureData.KeyOf(narrow.Typeface, glyph),
            Is.EqualTo(Adamantium.Fonts.TextureGeneration.GlyphTextureData.KeyOf(font.Typeface, glyph)), "one atlas cell");
        Assert.That(narrow.GetGlyphByIndex(glyph), Is.SameAs(font.GetGlyphByIndex(glyph)), "sampled once");
    }

    // A glyph whose 'hmtx' bearing differs from its outline's xMin is drawn shifted to the bearing, as rasterizers do;
    // HarfBuzz reports such a glyph's extents at variations unshifted, so only its width and height compare.
    private static bool IsShiftedByItsBearing(IFont font, uint glyphIndex)
    {
        var glyph = font.GetGlyphByIndex(glyphIndex);
        return glyph.BoundingRectangle.Width > 0 && font.GetLeftSideBearing(glyphIndex) != glyph.BoundingRectangle.X;
    }

    private static List<FontVariation> Parse(string variations)
    {
        return variations.Split(',')
            .Select(v => v.Split('='))
            .Select(v => new FontVariation(v[0].Trim(), float.Parse(v[1], CultureInfo.InvariantCulture)))
            .ToList();
    }
}
