using System;
using System.IO;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class GlyphRowTests
{
    private const string Letters = "3D-плиткиflipHOxopgjyбё";
    private const double Tolerance = 1e-3;

    [TestCase("SourceSans3-Regular.ttf")]
    [TestCase("SourceSans3-Regular.otf")]
    public void EveryGlyph_StandsWhereTheFontDrewIt_OnAWholePixelBaseline(string file)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", file));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);

        for (var size = 8.0; size <= 40; size += 0.25)
        {
            layout.ProcessText(Letters, size, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            var scale = size / layout.Font.UnitsPerEm;
            var glyphs = layout.GetTextData();
            var baseline = glyphs[0].Rect.Bottom + glyphs[0].Glyph.BoundingRectangle.Y * scale;

            Assert.That(baseline, Is.EqualTo(Math.Round(baseline)).Within(Tolerance), $"{size}px: baseline");
            foreach (var glyph in glyphs)
            {
                var bounds = glyph.Glyph.BoundingRectangle;
                Assert.That(glyph.Rect.Top, Is.EqualTo(baseline - (bounds.Y + bounds.Height) * scale).Within(Tolerance),
                    $"{size}px: top of {glyph.Symbol}");
                Assert.That(glyph.Rect.Bottom, Is.EqualTo(baseline - bounds.Y * scale).Within(Tolerance),
                    $"{size}px: bottom of {glyph.Symbol}");
            }
        }
    }
}
