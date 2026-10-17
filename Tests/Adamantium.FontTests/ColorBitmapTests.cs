using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class ColorBitmapTests
{
    private static readonly ConcurrentDictionary<string, IFont> Fonts = new();

    public static IEnumerable<TestCaseData> ReferenceCases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "ColorBitmaps", "expected.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t', 3);
            yield return new TestCaseData(fields[0], int.Parse(fields[1], CultureInfo.InvariantCulture),
                    fields.Length > 2 ? fields[2] : string.Empty)
                .SetName($"{Path.GetFileName(fields[0])} at {fields[1]} ppem");
        }
    }

    [TestCaseSource(nameof(ReferenceCases))]
    public void FindsTheImagesHarfBuzzFinds(string fontPath, int pixelsPerEm, string expected)
    {
        var font = Fonts.GetOrAdd(fontPath, p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(Describe(font, pixelsPerEm), Is.EqualTo(expected));
    }

    // HarfBuzz reads only index formats 1 and 3 and image format 17, which the subset has: the other formats were made
    // from it, so each must find the same images where the subset does.
    [TestCase("ColorFonts/NotoColorEmoji.subset.index_format2_image_format19.ttf")]
    [TestCase("ColorFonts/NotoColorEmoji.subset.index_format4_image_format18.ttf")]
    [TestCase("ColorFonts/NotoColorEmoji.subset.index_format5_image_format19.ttf")]
    public void TheOtherFormats_FindTheImagesTheSubsetHas(string variant)
    {
        var subset = Fonts.GetOrAdd("ColorFonts/NotoColorEmoji.subset.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));
        var font = Fonts.GetOrAdd(variant, p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(Describe(font, 0), Is.Not.Empty.And.EqualTo(Describe(subset, 0)));
        Assert.That(Describe(font, 109), Is.EqualTo(Describe(subset, 109)));
    }

    [Test]
    public void AFontWithoutImages_HasNone()
    {
        var font = Fonts.GetOrAdd("TTFFonts/CascadiaCode-Regular.ttf", p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(font.ColorBitmapSizes, Is.Empty);
        Assert.That(font.GetColorBitmap(font.GetGlyphByUnicode('a').Index, 0), Is.Null);
    }

    [Test]
    public void TheSizesAreTheStrikes()
    {
        var font = Fonts.GetOrAdd("ColorFonts/NotoColorEmoji.subset.multiple_size_tables.ttf",
            p => Typeface.LoadFont(p, 3).GetFont(0));

        Assert.That(font.ColorBitmapSizes, Has.Count.EqualTo(2));
        Assert.That(font.GetColorBitmap(1, 0).PixelsPerEm, Is.EqualTo(Max(font.ColorBitmapSizes)));
    }

    [Test]
    public void ALocationTableWithAnOffsetPastItsEnd_GivesNoImage()
    {
        var locations = new byte[8 + 48];
        locations[7] = 1;
        locations[8] = 0x80;
        locations[8 + 11] = 1;
        locations[8 + 44] = 109;
        locations[8 + 45] = 109;

        var table = Adamantium.Fonts.Tables.EmbeddedColorBitmapTable.Create(locations, new byte[16]);

        Assert.That(table.GetBitmap(1, 0), Is.Null);
    }

    private static string Describe(IFont font, int pixelsPerEm)
    {
        var text = new StringBuilder();
        for (uint glyph = 0; glyph < font.Typeface.GlyphCount; glyph++)
        {
            var bitmap = font.GetColorBitmap(glyph, pixelsPerEm);
            if (bitmap == null)
            {
                continue;
            }

            double scale = (double)font.UnitsPerEm / bitmap.PixelsPerEm;
            text.Append(text.Length > 0 ? "\t" : string.Empty)
                .Append($"{glyph}={bitmap.Png.Length}:{Hash(bitmap.Png):x8}:")
                .Append($"{Round(bitmap.Left * scale)},{Round(bitmap.Top * scale)},")
                .Append($"{Round(bitmap.Width * scale)},{-Round(bitmap.Height * scale)}");
        }

        return text.ToString();
    }

    private static int Max(IReadOnlyList<int> sizes)
    {
        var max = 0;
        foreach (var size in sizes)
        {
            max = Math.Max(max, size);
        }

        return max;
    }

    private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private static uint Hash(byte[] data)
    {
        var hash = 2166136261u;
        foreach (var b in data)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}
