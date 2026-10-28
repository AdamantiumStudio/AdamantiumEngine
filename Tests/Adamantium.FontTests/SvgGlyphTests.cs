using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Svg;
using Adamantium.Fonts.Tables;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class SvgGlyphTests
{
    private readonly List<List<List<OutlinePoint>>> outlines = [];

    private ColorPaintOperation[] Convert(string body)
    {
        outlines.Clear();
        var document = new SvgDocument(XDocument.Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">" + body + "</svg>"));
        return SvgGlyphConverter.Convert(document, 5, null, 0, (_, contours) =>
        {
            outlines.Add(contours);
            return (uint)(1000 + outlines.Count - 1);
        });
    }

    private static string Kinds(IEnumerable<ColorPaintOperation> operations) =>
        string.Join(" ", operations.Select(o => o.Kind.ToString()));

    [Test]
    public void AFilledPath_IsAClipByItsOutlineAroundAFill()
    {
        var operations = Convert("<path id=\"glyph5\" d=\"M0 0 L100 0 L100 -50 Z\" fill=\"#ff0000\"/>");

        Assert.That(Kinds(operations), Is.EqualTo("PushClip Fill PopClip"));
        Assert.That(operations[0].GlyphIndex, Is.EqualTo(1000u));
        Assert.That(operations[1].Fill.Stops.Single().Color, Is.EqualTo(Color.FromRgba(255, 0, 0, 255)));
        Assert.That(outlines[0][0].Select(p => (p.X, p.Y)), Is.EqualTo(new[] { (0.0, 0.0), (100.0, 0.0), (100.0, 50.0) }),
            "y points down in SVG and up in the font");
    }

    [TestCase("fill=\"none\"", TestName = "Unfilled")]
    [TestCase("display=\"none\"", TestName = "Not displayed")]
    [TestCase("style=\"display:none\"", TestName = "Not displayed by style")]
    public void AShapeThatShowsNothing_HasNoSteps(string attribute)
    {
        Assert.That(Convert($"<rect id=\"glyph5\" width=\"10\" height=\"10\" {attribute}/>"), Is.Empty);
    }

    [Test]
    public void CurrentColor_TakesTheTextsColor_UnlessTheDocumentSetsOne()
    {
        var text = Convert("<rect id=\"glyph5\" width=\"10\" height=\"10\" fill=\"currentColor\" fill-opacity=\"0.5\"/>");
        var own = Convert("<g id=\"glyph5\" color=\"lime\"><rect width=\"10\" height=\"10\" fill=\"currentColor\"/></g>");

        Assert.That(text[1].Fill.Stops.Single().Color, Is.Null);
        Assert.That(text[1].Fill.Stops.Single().Alpha, Is.EqualTo(0.5f));
        Assert.That(own[1].Fill.Stops.Single().Color, Is.EqualTo(Color.FromRgba(0, 255, 0, 255)));
    }

    [TestCase("#0f08", 0, 255, 0, 136)]
    [TestCase("rgb(10%, 20, 30)", 26, 20, 30, 255)]
    [TestCase("var(--color3, #102030)", 16, 32, 48, 255)]
    [TestCase("Tomato", 255, 99, 71, 255)]
    public void AColorIsReadAsCssWritesIt(string fill, int r, int g, int b, int a)
    {
        var operations = Convert($"<rect id=\"glyph5\" width=\"10\" height=\"10\" fill=\"{fill}\"/>");

        Assert.That(operations[1].Fill.Stops.Single().Color, Is.EqualTo(Color.FromRgba((byte)r, (byte)g, (byte)b, (byte)a)));
    }

    [Test]
    public void AGradientInBoundingBoxUnits_SpansTheShape()
    {
        var operations = Convert(
            "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"100%\" stop-color=\"blue\" stop-opacity=\"0.5\"/></linearGradient></defs>" +
            "<rect id=\"glyph5\" x=\"10\" y=\"-40\" width=\"200\" height=\"40\" fill=\"url(#g)\"/>");
        var fill = operations[1].Fill;

        Assert.That(fill.Kind, Is.EqualTo(ColorFillKind.LinearGradient));
        Assert.That(Vector2<double>.Transform(fill.Point0, fill.Transform), Is.EqualTo(new Vector2<double>(10, 40)));
        Assert.That(Vector2<double>.Transform(fill.Point1, fill.Transform), Is.EqualTo(new Vector2<double>(210, 40)));
        Assert.That(fill.Stops.Select(s => s.Color?.A), Is.EqualTo(new byte?[] { 255, 128 }));
    }

    [Test]
    public void AGradient_TakesWhatItLacksFromTheOneItNames()
    {
        var operations = Convert(
            "<defs><radialGradient id=\"base\" spreadMethod=\"reflect\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></radialGradient>" +
            "<radialGradient id=\"g\" xlink:href=\"#base\" gradientUnits=\"userSpaceOnUse\" cx=\"5\" cy=\"5\" r=\"4\"/></defs>" +
            "<circle id=\"glyph5\" cx=\"5\" cy=\"5\" r=\"4\" fill=\"url(#g)\"/>");
        var fill = operations[1].Fill;

        Assert.That(fill.Kind, Is.EqualTo(ColorFillKind.RadialGradient));
        Assert.That(fill.Extend, Is.EqualTo(ColorExtend.Reflect));
        Assert.That(fill.Stops, Has.Count.EqualTo(2));
        Assert.That((fill.Point1, fill.Radius1), Is.EqualTo((new Vector2<double>(5, 5), 4.0)));
    }

    [Test]
    public void AUse_DrawsWhatItNames_MovedByItsTransformAndPlace()
    {
        Convert("<defs><rect id=\"r\" width=\"10\" height=\"10\"/></defs>" +
                "<g id=\"glyph5\" transform=\"scale(2)\"><use xlink:href=\"#r\" x=\"5\" y=\"-20\"/></g>");

        var points = outlines.Single().Single();
        Assert.That(points.Min(p => p.X), Is.EqualTo(10));
        Assert.That(points.Max(p => p.Y), Is.EqualTo(40));
    }

    [Test]
    public void AGroupsOpacity_FadesTheGroupAsAWhole()
    {
        var operations = Convert("<g id=\"glyph5\" opacity=\"0.5\"><rect width=\"10\" height=\"10\" fill=\"red\"/>" +
                                 "<rect x=\"5\" width=\"10\" height=\"10\" fill=\"blue\"/></g>");

        Assert.That(Kinds(operations), Is.EqualTo(
            "PushGroup PushClip Fill PopClip PushClip Fill PopClip PushGroup Fill PopGroup PopGroup"));
        Assert.That(operations[^2].Mode, Is.EqualTo(ColorCompositeMode.DestinationIn));
        Assert.That(operations[^3].Fill.Stops.Single().Color?.A, Is.EqualTo(128));
        Assert.That(operations[2].Fill.Stops.Single().Color?.A, Is.EqualTo(255), "the shapes inside are not faded twice");
    }

    [Test]
    public void TheGlyphsAncestors_MoveAndStyleIt()
    {
        Convert("<g transform=\"translate(100 0)\" fill=\"lime\"><rect id=\"glyph5\" width=\"10\" height=\"10\"/></g>");

        Assert.That(outlines.Single().Single().Min(p => p.X), Is.EqualTo(100));
    }

    [Test]
    public void AGradientInBoundingBoxUnits_SpansTheCurveNotItsHandles()
    {
        var operations = Convert(
            "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient></defs>" +
            "<path id=\"glyph5\" d=\"M0 0 C0 -40 100 -40 100 0 Z\" fill=\"url(#g)\"/>");
        var fill = operations[1].Fill;

        var top = Vector2<double>.Transform(new Vector2<double>(0, 0), fill.Transform).Y;
        Assert.That(top, Is.EqualTo(30).Within(1e-6), "the curve rises 30, its handles 40");
    }

    [Test]
    public void AClipPath_ClipsWhatItHolds()
    {
        var operations = Convert(
            "<defs><clipPath id=\"c\"><circle cx=\"0\" cy=\"0\" r=\"5\"/></clipPath></defs>" +
            "<g id=\"glyph5\" clip-path=\"url(#c)\"><rect width=\"10\" height=\"10\" fill=\"red\"/></g>");

        Assert.That(Kinds(operations), Is.EqualTo("PushClip PushClip Fill PopClip PopClip"));
    }

    // A box from 20,10 to 60,50 in its own units: the clip's 0 to 1 spans it.
    [TestCase("", TestName = "Of a shape")]
    [TestCase(" transform=\"translate(0 0)\"", TestName = "Of a group")]
    public void AClipPathInBoundingBoxUnits_SpansWhatItClips(string group)
    {
        var operations = Convert(
            "<defs><clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect width=\"0.5\" height=\"1\"/></clipPath></defs>" +
            $"<g id=\"glyph5\"{group}><g clip-path=\"url(#c)\"><rect x=\"20\" y=\"10\" width=\"40\" height=\"40\" fill=\"red\"/></g></g>");

        Assert.That(Kinds(operations), Is.EqualTo("PushClip PushClip Fill PopClip PopClip"));
        var clip = outlines[0].SelectMany(contour => contour).ToList();
        Assert.That(clip.Min(p => p.X), Is.EqualTo(20).Within(1e-9));
        Assert.That(clip.Max(p => p.X), Is.EqualTo(40).Within(1e-9), "half the box across");
        Assert.That(clip.Min(p => p.Y), Is.EqualTo(-50).Within(1e-9));
        Assert.That(clip.Max(p => p.Y), Is.EqualTo(-10).Within(1e-9), "all of it down, y up in the font");
    }

    private const string HalfClip =
        "<clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect width=\"0.5\" height=\"1\"/></clipPath>";

    private (double Left, double Right) ClipAcross()
    {
        var clip = outlines[0].SelectMany(contour => contour).ToList();
        return (clip.Min(p => p.X), clip.Max(p => p.X));
    }

    // The box placed as the walk draws it: a use's place and its target's transform once, a symbol only where used.
    [TestCase("<rect id=\"r\" x=\"20\" width=\"40\" height=\"40\"/>",
        "<use id=\"glyph5\" href=\"#r\" x=\"100\" clip-path=\"url(#c)\" fill=\"red\"/>", 120, 140, TestName = "A use moved")]
    [TestCase("<rect id=\"r\" transform=\"translate(100 0)\" x=\"20\" width=\"40\" height=\"40\"/>",
        "<g id=\"glyph5\" clip-path=\"url(#c)\"><use href=\"#r\" fill=\"red\"/></g>", 120, 140,
        TestName = "A use of a moved target")]
    [TestCase("<symbol id=\"s\"><rect x=\"500\" width=\"10\" height=\"10\"/></symbol>",
        "<g id=\"glyph5\" clip-path=\"url(#c)\"><rect x=\"20\" width=\"40\" height=\"40\" fill=\"red\"/></g>", 20, 40,
        TestName = "A symbol nothing uses")]
    public void AClipPathInBoundingBoxUnits_TakesTheBoxAsItIsDrawn(string defs, string glyph, double left, double right)
    {
        Convert($"<defs>{HalfClip}{defs}</defs>{glyph}");

        Assert.That(ClipAcross(), Is.EqualTo((left, right)));
    }

    // Browsers map the box first, then the clip's own transform, in the units the box gives.
    [Test]
    public void AClipPathInBoundingBoxUnits_MovesByItsTransformAfterTheBox()
    {
        Convert("<defs><clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\" transform=\"translate(0.25 0)\">" +
                "<rect width=\"0.5\" height=\"1\"/></clipPath></defs>" +
                "<rect id=\"glyph5\" x=\"20\" width=\"40\" height=\"40\" clip-path=\"url(#c)\" fill=\"red\"/>");

        var (left, right) = ClipAcross();
        Assert.That(left, Is.EqualTo(20.25).Within(1e-6));
        Assert.That(right, Is.EqualTo(40.25).Within(1e-6));
    }

    [Test]
    public void AClipPathInBoundingBoxUnits_OfUsesOfUses_IsFoundInTime()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Convert($"<defs>{HalfClip}<g id=\"a\"><use href=\"#a\"/><use href=\"#a\"/><use href=\"#a\"/><use href=\"#a\"/>" +
                "<rect width=\"1\" height=\"1\"/></g></defs><g id=\"glyph5\" clip-path=\"url(#c)\"><use href=\"#a\"/></g>");

        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    // A use in a clip path takes its target's transform; the clip path's clip-rule reaches its children.
    [Test]
    public void AClipPath_TakesItsUsesTransformsAndItsOwnClipRule()
    {
        Convert("<defs><rect id=\"r\" transform=\"translate(100 0)\" width=\"10\" height=\"10\"/>" +
                "<clipPath id=\"c\" clip-rule=\"evenodd\"><use href=\"#r\"/></clipPath></defs>" +
                "<rect id=\"glyph5\" width=\"200\" height=\"10\" clip-path=\"url(#c)\" fill=\"red\"/>");

        Assert.That(ClipAcross(), Is.EqualTo((100.0, 110.0)));
    }

    // A presentation attribute of a value it does not take is dropped: the fill is inherited, black at the top.
    [Test]
    public void AWrongFillAttribute_LeavesTheFillInherited()
    {
        var operations = Convert("<g id=\"glyph5\" fill=\"lime\"><rect width=\"1\" height=\"1\" fill=\"bogus\"/></g>");

        Assert.That(operations.Single(o => o.Kind == ColorPaintOperationKind.Fill).Fill.Stops.Single().Color,
            Is.EqualTo(Color.FromRgba(0, 255, 0, 255)));
    }

    [Test]
    public void AGlyphUnderAHiddenAncestor_DrawsNothing()
    {
        Assert.That(Convert("<g display=\"none\"><rect id=\"glyph5\" width=\"1\" height=\"1\"/></g>"), Is.Empty);
    }

    [Test]
    public void AnAncestorsClipPath_ClipsTheGlyph()
    {
        var operations = Convert("<defs><clipPath id=\"c\"><rect x=\"2\" width=\"3\" height=\"10\"/></clipPath></defs>" +
                                 "<g clip-path=\"url(#c)\"><rect id=\"glyph5\" width=\"10\" height=\"10\" fill=\"red\"/></g>");

        Assert.That(Kinds(operations), Is.EqualTo("PushClip PushClip Fill PopClip PopClip"));
        Assert.That(ClipAcross(), Is.EqualTo((2.0, 5.0)));
    }

    [Test]
    public void AClipPathInBoundingBoxUnits_OfNothing_ClipsEverything()
    {
        var operations = Convert(
            "<defs><clipPath id=\"c\" clipPathUnits=\"objectBoundingBox\"><rect width=\"1\" height=\"1\"/></clipPath></defs>" +
            "<g id=\"glyph5\" clip-path=\"url(#c)\"/>");

        Assert.That(Kinds(operations), Is.EqualTo("PushClip PopClip"));
        Assert.That(outlines[0], Is.Empty);
    }

    [Test]
    public void AStyleSheet_StylesByTagClassAndId()
    {
        var operations = Convert(
            "<style>/* a sheet */ .a { fill: #00ff00 } rect.b { fill-opacity: 50% } #c { fill: blue }</style>" +
            "<g id=\"glyph5\"><rect class=\"a\" width=\"1\" height=\"1\"/><rect class=\"a b\" width=\"1\" height=\"1\"/>" +
            "<rect id=\"c\" class=\"a\" width=\"1\" height=\"1\"/></g>");
        var fills = operations.Where(o => o.Kind == ColorPaintOperationKind.Fill).Select(o => o.Fill.Stops.Single()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(fills[0].Color, Is.EqualTo(Color.FromRgba(0, 255, 0, 255)), "by class");
            Assert.That(fills[1].Color, Is.EqualTo(Color.FromRgba(0, 255, 0, 128)), "half opaque by tag and class");
            Assert.That(fills[2].Color, Is.EqualTo(Color.FromRgba(0, 0, 255, 255)), "the id outranks the class");
        });
    }

    // Important in the sheet, then the style attribute, the sheet, the presentation attribute.
    [TestCase(".a { fill: red }", "fill=\"blue\"", "", 255, 0, 0)]
    [TestCase(".a { fill: red }", "", "fill: lime", 0, 255, 0)]
    [TestCase(".a { fill: red !important }", "", "fill: lime", 255, 0, 0)]
    [TestCase(".a { fill: red } .a { fill: blue }", "", "", 0, 0, 255)]
    [TestCase("g .a { fill: red } .a:hover { fill: red } [x] { fill: red } @media print { .a { fill: red } }",
        "fill=\"blue\"", "", 0, 0, 255, TestName = "Selectors left out")]
    [TestCase(".a, g > .a { fill: red }", "fill=\"blue\"", "", 0, 0, 255, TestName = "A rule with one selector left out")]
    [TestCase(".a { fill: red; fill: blue }", "", "", 0, 0, 255, TestName = "The later declaration of a rule")]
    [TestCase("@charset \"utf-8\"; @import url(\"x.css\"); .a { fill: red }", "fill=\"blue\"", "", 255, 0, 0,
        TestName = "After statement at-rules")]
    [TestCase("", "", "fill: red !important; fill: blue", 255, 0, 0, TestName = "Important in the style attribute")]
    [TestCase(".a { fill: red ! important }", "", "fill: blue", 255, 0, 0, TestName = "Important spaced")]
    [TestCase(".a { FILL: red }", "fill=\"blue\"", "", 255, 0, 0, TestName = "A property in capitals")]
    [TestCase("#glyph5#glyph5 { fill: red } #glyph5.a { fill: blue }", "", "", 255, 0, 0, TestName = "Two ids")]
    [TestCase(".a.b.c.d.e.f.g.h.i.j.k { fill: blue } #glyph5 { fill: red }", "", "", 255, 0, 0,
        TestName = "An id over many classes")]
    [TestCase(".a { font-family: \"x{;}\"; fill: red } .z { fill: blue }", "fill=\"blue\"", "", 255, 0, 0,
        TestName = "Braces and semicolons in quotes")]
    [TestCase(".a { fill: bogus }", "fill=\"blue\"", "", 0, 0, 255, TestName = "A value the sheet gets wrong")]
    [TestCase(".a { fill: red } #glyph5 { fill: 12px }", "", "", 255, 0, 0, TestName = "A wrong value of a stronger rule")]
    [TestCase(".a { fill: red }", "", "fill: nonsense", 255, 0, 0, TestName = "A value the style gets wrong")]
    public void AStyleSheet_CascadesAsCssDoes(string sheet, string attribute, string style, int r, int g, int b)
    {
        var operations = Convert($"<style>{sheet}</style><rect id=\"glyph5\" class=\"a b c d e f g h i j k\" width=\"1\" " +
                                 $"height=\"1\" {attribute} style=\"{style}\"/>");

        Assert.That(operations.Single(o => o.Kind == ColorPaintOperationKind.Fill).Fill.Stops.Single().Color,
            Is.EqualTo(Color.FromRgba((byte)r, (byte)g, (byte)b, 255)));
    }

    [Test]
    public void EvenOdd_TurnsAContourInsideAnotherTheOtherWay()
    {
        Convert("<path id=\"glyph5\" fill-rule=\"evenodd\" d=\"M0 0 H30 V30 H0 Z M10 10 H20 V20 H10 Z\"/>");

        var contours = outlines.Single();
        Assert.That(Math.Sign(Area(contours[0])), Is.Not.EqualTo(Math.Sign(Area(contours[1]))), "the hole winds the other way");
    }

    [Test]
    public void AnArc_IsCurvesOfAQuarterTurnAtMost()
    {
        Convert("<path id=\"glyph5\" d=\"M9,5 A4 4 0 1 1 1,5 A4 4 0 1 1 9,5 Z\"/>");

        var points = outlines.Single().Single();
        Assert.That(points.Count(p => p.IsControl), Is.EqualTo(8), "a circle is four cubic curves");
        Assert.That(points.Where(p => !p.IsControl).All(p => Math.Abs(Math.Sqrt((p.X - 5) * (p.X - 5) + (p.Y + 5) * (p.Y + 5)) - 4) < 1e-9));
    }

    [Test]
    public void ACompressedDocument_IsRead()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect id=\"glyph5\" width=\"1\" height=\"1\"/></svg>");
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
        {
            gzip.Write(svg, 0, svg.Length);
        }

        var document = compressed.ToArray();
        var table = new byte[10 + 2 + 12 + document.Length];
        table[5] = 10;
        table[11] = 1;
        table[13] = 5;
        table[15] = 5;
        table[19] = 14;
        table[20] = (byte)(document.Length >> 24);
        table[21] = (byte)(document.Length >> 16);
        table[22] = (byte)(document.Length >> 8);
        table[23] = (byte)document.Length;
        Array.Copy(document, 0, table, 24, document.Length);

        Assert.That(SvgDocumentTable.Create(table).GetDocument(5)?.Find("glyph5"), Is.Not.Null);
    }

    [Test]
    public void TheSameDrawings_WrittenAsPicoSvgAndAsTheyWere_HaveTheSameOutlines()
    {
        var pico = Typeface.LoadFont("ColorFonts/samples-picosvg.ttf", 3).GetFont(0);
        var untouched = Typeface.LoadFont("ColorFonts/samples-untouchedsvg.ttf", 3).GetFont(0);
        for (uint glyph = 19; glyph < 28; glyph++)
        {
            var expected = Extents(pico, glyph);
            var actual = Extents(untouched, glyph);
            Assert.That(actual, Has.Count.EqualTo(expected.Count), $"glyph {glyph}");
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(0.05), $"glyph {glyph}: picosvg rounds the numbers");
            }
        }
    }

    private static List<double> Extents(IFont font, uint glyph)
    {
        var extents = new List<double>();
        foreach (var clip in font.GetColorPaint(glyph).Where(o => o.Kind == ColorPaintOperationKind.PushClip))
        {
            var points = font.GetGlyphByIndex(clip.GlyphIndex).Outlines.SelectMany(o => o.Points).ToList();
            extents.AddRange([points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y)]);
        }

        return extents;
    }

    private static double Area(List<OutlinePoint> contour)
    {
        var area = 0.0;
        for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
        {
            area += contour[j].X * contour[i].Y - contour[i].X * contour[j].Y;
        }

        return area;
    }
}
