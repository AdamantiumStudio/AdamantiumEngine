using System.Linq;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

// A glyph's outline handed out as segments: what draws the letter as a path - a stroked letter, a letter that clips.
public class GlyphContourTests
{
    [TestCase("TTFFonts/SourceSans3-Regular.ttf", GlyphSegmentKind.Quadratic)]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", GlyphSegmentKind.Cubic)]
    public void TheContours_AreClosedRunsOfLinesAndCurves(string path, GlyphSegmentKind curves)
    {
        var typeface = Typeface.LoadFont(path, 3);
        var font = typeface.Fonts[0];
        var o = font.GetGlyphByCharacter('O');

        var contours = o.GetContours();

        Assert.Multiple(() =>
        {
            Assert.That(contours, Has.Count.EqualTo(2), "the outside and the counter");
            foreach (var contour in contours)
            {
                var segments = contour.Segments;
                Assert.That(segments.Any(s => s.Kind == curves), "curves of the font's own degree");
                Assert.That(segments.All(s => s.Kind == GlyphSegmentKind.Line || s.Kind == curves));
                for (var i = 1; i < segments.Count; i++)
                {
                    Assert.That(segments[i].Start, Is.EqualTo(segments[i - 1].End), "each segment starts where the last ends");
                }

                Assert.That(segments[^1].End, Is.EqualTo(segments[0].Start), "closed");
            }

            var bounds = o.BoundingRectangle;
            var xs = contours.SelectMany(c => c.Segments).SelectMany(s => new[] { s.Start.X, s.End.X }).ToArray();
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
    public void EachCall_GivesItsOwnCopy()
    {
        var font = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).Fonts[0];
        var h = font.GetGlyphByCharacter('H');

        var first = h.GetContours();
        var second = h.GetContours();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second[0].Segments, Is.EqualTo(first[0].Segments), "the same outline");
        });
    }
}
