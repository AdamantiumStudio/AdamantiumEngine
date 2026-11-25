using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Subsetting;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

// A subset written from outlines and read back: the same shapes, at 1000 units to the em, under their own CIDs.
public class CffSubsetTests
{
    private static readonly string[] Files =
        Directory.Exists("TTFFonts")
            ? Directory.GetFiles("TTFFonts", "*.ttf", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("OTFFonts", "*.otf", SearchOption.AllDirectories)).OrderBy(file => file).ToArray()
            : [];

    [Test]
    public void EachFont_IsReadBackAsItIsDrawn()
    {
        Assume.That(Files, Is.Not.Empty);
        var refused = new List<string>();
        foreach (var file in Files)
        {
            IFont font;
            try
            {
                font = Typeface.LoadFont(file, 3).GetFont(0);
            }
            catch (System.Exception)
            {
                refused.Add(file);   // a file kept to be refused
                continue;
            }

            var asked = Enumerable.Range(0, (int)font.Typeface.GlyphCount).Where(index => index % 11 == 5).Select(index => (uint)index).ToList();
            var written = CffSubset.IndicesOf(font, asked);
            var read = Typeface.LoadCff(CffSubset.Write(font, asked, "ABCDEF+Probe"));

            Assert.That(read.IsCidKeyed, Is.True, file);
            Assert.That(read.GlyphCount, Is.EqualTo(written.Count), file);
            Assert.That(read.CurrentFont.UnitsPerEm, Is.EqualTo(CffSubset.UnitsPerEm), file);
            for (var at = 0; at < written.Count; at++)
            {
                read.GetGlyphByIndex((uint)at, out var glyph);
                Assert.That(read.GetGlyphByCid(written[at], out var named) && ReferenceEquals(named, glyph), Is.True,
                    $"{file}, CID {written[at]}");
                var scale = (double)CffSubset.UnitsPerEm / font.UnitsPerEm;
                AssertSame(font.GetGlyphByIndex(written[at]), glyph, scale, $"{file}, glyph {written[at]}");
            }
        }

        Assert.That(refused.Count, Is.LessThan(Files.Length / 10), string.Join(", ", refused));
    }

    [Test]
    public void AGlyphAskedForTwice_OrNotThere_IsWrittenOnceOrNotAtAll()
    {
        var font = Typeface.LoadFont("OTFFonts/SourceSans3-Regular.otf", 3).GetFont(0);

        Assert.That(CffSubset.IndicesOf(font, [7, 3, 7, font.Typeface.GlyphCount, 90000]), Is.EqualTo(new uint[] { 0, 3, 7 }));
        Assert.That(Typeface.LoadCff(CffSubset.Write(font, [], "x")).GlyphCount, Is.EqualTo(1));
    }

    // A TrueType contour may have no point on it at all: it starts midway between its last control and its first.
    [Test]
    public void AContourOfControlsAlone_StartsBetweenThem()
    {
        var glyph = new Glyph(1, Fonts.Common.OutlineType.TrueType);
        var outline = new Fonts.Common.Outline();
        foreach (var (x, y) in new[] { (0, 0), (100, 0), (100, 100), (0, 100) })
        {
            outline.Points.Add(new Fonts.Common.OutlinePoint(x, y, true));
        }

        glyph.AddOutline(outline);

        var segments = glyph.GetContours().Single().Segments;
        Assert.That(segments, Has.Count.EqualTo(4));
        Assert.That(segments.Select(segment => segment.Start), Is.EqualTo(new[]
        {
            new Vector2<double>(0, 50), new Vector2<double>(50, 0), new Vector2<double>(100, 50), new Vector2<double>(50, 100),
        }));
        Assert.That(segments[^1].End, Is.EqualTo(segments[0].Start));
    }

    private static void AssertSame(Glyph original, Glyph read, double scale, string what)
    {
        var one = Points(original, scale);
        var two = Points(read, 1);
        Assert.That(two.Count, Is.EqualTo(one.Count), what);
        for (var at = 0; at < one.Count; at++)
        {
            Assert.That((one[at] - two[at]).Length(), Is.LessThan(0.02), $"{what}, point {at}");
        }
    }

    // Where each piece ends and where its middle lies, a closing line or a line of no length left out.
    private static List<Vector2<double>> Points(Glyph glyph, double scale)
    {
        var points = new List<Vector2<double>>();
        foreach (var contour in glyph.GetContours())
        {
            var pieces = contour.Segments
                .Where(piece => piece.Kind != GlyphSegmentKind.Line || piece.Start != piece.End).ToList();
            if (pieces.Count == 0)
            {
                continue;
            }

            var start = pieces[0].Start;
            points.Add(start * scale);
            for (var at = 0; at < pieces.Count; at++)
            {
                var piece = pieces[at];
                if (piece.Kind == GlyphSegmentKind.Line && at == pieces.Count - 1 && (piece.End - start).Length() * scale < 0.02)
                {
                    continue;
                }

                var middle = piece.Kind switch
                {
                    GlyphSegmentKind.Line => (piece.Start + piece.End) * 0.5,
                    GlyphSegmentKind.Quadratic => piece.Start * 0.25 + piece.Control1 * 0.5 + piece.End * 0.25,
                    _ => piece.Start * 0.125 + piece.Control1 * 0.375 + piece.Control2 * 0.375 + piece.End * 0.125,
                };
                points.Add(middle * scale);
                points.Add(piece.End * scale);
            }
        }

        return points;
    }
}
