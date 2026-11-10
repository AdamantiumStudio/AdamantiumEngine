using System.Linq;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

// A glyph's outline handed out as segments: what draws the letter as a path - a stroked letter, a letter that clips.
public class GlyphContourTests
{
    [TestCase("TTFFonts/SourceSans3-Regular.ttf", 3)]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", 4)]
    public void TheContours_AreClosedRunsOfLinesAndCurves(string path, int curvePoints)
    {
        var typeface = Typeface.LoadFont(path, 3);
        var font = typeface.Fonts[0];
        var o = font.GetGlyphByCharacter('O');

        var contours = o.GetContours();

        Assert.Multiple(() =>
        {
            Assert.That(contours, Has.Length.EqualTo(2), "the outside and the counter");
            foreach (var contour in contours)
            {
                Assert.That(contour.All(s => s.Length is >= 2 and <= 4));
                Assert.That(contour.Any(s => s.Length == curvePoints), "curves of the font's own degree");
                for (var i = 1; i < contour.Length; i++)
                {
                    Assert.That(contour[i][0], Is.EqualTo(contour[i - 1][^1]), "each segment starts where the last ends");
                }

                Assert.That(contour[^1][^1], Is.EqualTo(contour[0][0]), "closed");
            }

            var bounds = o.BoundingRectangle;
            var xs = contours.SelectMany(c => c.SelectMany(s => s)).Select(p => p.X).ToArray();
            Assert.That(xs.Min(), Is.GreaterThanOrEqualTo(bounds.Left - 1));
            Assert.That(xs.Max(), Is.LessThanOrEqualTo(bounds.Right + 1));
        });
    }

    [Test]
    public void ASpace_HasNoContours()
    {
        var font = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).Fonts[0];

        Assert.That(font.GetGlyphByCharacter(' ').GetContours(), Is.Empty);
    }

    [Test]
    public void TheCopy_IsTheCallers()
    {
        var font = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).Fonts[0];
        var h = font.GetGlyphByCharacter('H');
        var first = h.GetContours();
        var original = first[0][0][0];

        first[0][0][0] = new Mathematics.Vector2<double>(-999, -999);

        Assert.That(h.GetContours()[0][0][0], Is.EqualTo(original));
    }
}
