using System.Linq;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Text;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>A page's text with positions: glyphs and their boxes from the text operators, lines, spaces, selection,
/// copying and search. The font is 500 wide for every code, ascent 800, descent -200, so every box is known.</summary>
public class PdfTextTests
{
    private const string Font =
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 32 /LastChar 255 /Widths [" +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 500 " +
        "500 500 500 500 500 500 500 500 500 500 500 500] /Encoding /WinAnsiEncoding /FontDescriptor 5 0 R >>";

    private static PdfPageText Text(string content, string resources = "", params string[] more)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Contents 6 0 R /Resources << /Font << /F1 4 0 R >> " +
                    resources + " >> >>");
        builder.Add(Font);
        builder.Add("<< /Type /FontDescriptor /FontName /Helvetica /Flags 32 /Ascent 800 /Descent -200 >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, Encoding.Latin1.GetBytes(content)));
        foreach (var body in more)
        {
            builder.Add(body);
        }

        return PdfDocument.Open(builder.Build("/Root 1 0 R")).Pages[0].ReadText();
    }

    private static void AssertPoint(PdfPoint actual, double x, double y) =>
        Assert.That((actual.X, actual.Y), Is.EqualTo((x, y)).Using<(double, double)>((a, b) =>
            System.Math.Abs(a.Item1 - b.Item1) < 1e-6 && System.Math.Abs(a.Item2 - b.Item2) < 1e-6));

    [Test]
    public void AGlyphsBoxRunsAcrossItsAdvanceFromDescentToAscent()
    {
        var text = Text("BT /F1 10 Tf 100 700 Td (Hello world) Tj ET");

        Assert.That(text.Text, Is.EqualTo("Hello world"));
        Assert.That(text.Glyphs, Has.Count.EqualTo(11));
        var first = text.Glyphs[0];
        AssertPoint(first.Quad.BottomLeft, 100, 698);
        AssertPoint(first.Quad.TopRight, 105, 708);
        AssertPoint(text.Glyphs[10].End, 155, 700);
        Assert.That(first.Size, Is.EqualTo(10).Within(1e-9));
        Assert.That(first.FontName, Is.EqualTo("Helvetica"));
        Assert.That(text.Lines, Has.Count.EqualTo(1));
    }

    [Test]
    public void GlyphsApartGetASpaceAndANewBaselineALineBreak()
    {
        var text = Text("BT /F1 10 Tf 100 700 Td (Hello) Tj 30 0 Td (world) Tj 0 -14 Td (next) Tj " +
                        "[(ke) -2000 (rn)] TJ ET");

        Assert.That(text.Text, Is.EqualTo("Hello world\nnextke rn"), "ke follows next without a gap");
        Assert.That(text.Lines.Select(line => line.Count), Is.EqualTo(new[] { 10, 8 }));
        Assert.That(text.GetText(3, 12), Is.EqualTo("lo world\nne"));
    }

    [Test]
    public void SpacingScalingAndRiseMoveTheGlyphs()
    {
        var text = Text("BT /F1 10 Tf 2 Tc 3 Tw 50 Tz 4 Ts 0 0 Td (a b) Tj ET");

        AssertPoint(text.Glyphs[0].Origin, 0, 4);
        AssertPoint(text.Glyphs[1].Origin, (5 + 2) * 0.5, 4);
        AssertPoint(text.Glyphs[2].Origin, (5 + 2) * 0.5 + (5 + 2 + 3) * 0.5, 4);
    }

    [Test]
    public void TextTurnedByItsMatrixKeepsItsLineAndTurnedBoxes()
    {
        var text = Text("BT /F1 10 Tf 0 1 -1 0 300 100 Tm (up text) Tj ET");

        Assert.That(text.Lines, Has.Count.EqualTo(1));
        Assert.That(text.Text, Is.EqualTo("up text"));
        AssertPoint(text.Glyphs[0].Quad.BottomLeft, 302, 100);
        AssertPoint(text.Glyphs[0].Quad.TopRight, 292, 105);
        Assert.That(text.Find("text").Single().Quads.Single().Bounds,
            Is.EqualTo(new PdfRectangle(292, 115, 302, 135)));
    }

    [Test]
    public void FormsAreReadThroughTheirMatrixAndOneThatDrawsItselfStops()
    {
        var text = Text("q 2 0 0 2 0 0 cm /X1 Do Q BT /F1 10 Tf 0 0 Td (after) Tj ET",
            "/XObject << /X1 7 0 R >>",
            Encoding.Latin1.GetString(PdfFileBuilder.Stream(
                "/Type /XObject /Subtype /Form /BBox [0 0 600 800] /Matrix [1 0 0 1 10 20] " +
                "/Resources << /Font << /F1 4 0 R >> /XObject << /X1 7 0 R >> >>",
                Encoding.ASCII.GetBytes("BT /F1 10 Tf 0 0 Td (in) Tj ET /X1 Do"))));

        Assert.That(text.Text, Is.EqualTo("in\nafter"));
        AssertPoint(text.Glyphs[0].Origin, 20, 40);
        Assert.That(text.Glyphs[0].Size, Is.EqualTo(20).Within(1e-9));
        AssertPoint(text.Glyphs[2].Origin, 0, 0);
    }

    [Test]
    public void AMarkedSpansActualTextStandsForItsGlyphs()
    {
        var text = Text("BT /F1 10 Tf 0 0 Td /Span << /ActualText (fi) >> BDC (XY) Tj EMC (z) Tj ET");

        Assert.That(text.Text, Is.EqualTo("fiz"));
        Assert.That(text.Glyphs.Select(g => g.Text), Is.EqualTo(new[] { "fi", "", "z" }));
    }

    [Test]
    public void SearchIgnoresCaseAndAccentsUnlessAskedAndRunsAcrossLines()
    {
        var text = Text("BT /F1 10 Tf 100 700 Td (Caf\\351 cafe) Tj 0 -14 Td (World wide) Tj ET");

        Assert.That(text.Find("CAFE").Select(m => (m.Start, m.End)), Is.EqualTo(new[] { (0, 4), (5, 9) }));
        Assert.That(text.Find("cafe", new PdfTextSearchOptions { MatchDiacritics = true }).Single().Start,
            Is.EqualTo(5));
        Assert.That(text.Find("Cafe", new PdfTextSearchOptions { MatchCase = true }).Select(m => m.Start),
            Is.EqualTo(new[] { 0 }));
        var across = text.Find("cafe world").Single();
        Assert.That((across.Start, across.End), Is.EqualTo((5, 14)));
        Assert.That(across.Quads, Has.Count.EqualTo(2));
        Assert.That(text.Find("wid", new PdfTextSearchOptions { WholeWord = true }), Is.Empty);
        Assert.That(text.Find("wide", new PdfTextSearchOptions { WholeWord = true }), Has.Count.EqualTo(1));
    }

    [Test]
    public void TheCaretFallsBeforeTheGlyphWhoseFirstHalfIsHit()
    {
        var text = Text("BT /F1 10 Tf 100 700 Td (abcd) Tj 0 -20 Td (efgh) Tj ET");

        Assert.That(text.CaretAt(new PdfPoint(101, 702)), Is.EqualTo(0));
        Assert.That(text.CaretAt(new PdfPoint(104, 702)), Is.EqualTo(1));
        Assert.That(text.CaretAt(new PdfPoint(200, 703)), Is.EqualTo(4), "past the line's end");
        Assert.That(text.CaretAt(new PdfPoint(111, 650)), Is.EqualTo(6), "below the last line");
        var quads = text.QuadsOf(2, 6);
        Assert.That(quads.Select(q => q.Bounds), Is.EqualTo(new[]
        {
            new PdfRectangle(110, 698, 120, 708), new PdfRectangle(100, 678, 110, 688),
        }));
        Assert.That(quads[0].ToQuadPoints(), Is.EqualTo(new double[] { 110, 708, 120, 708, 110, 698, 120, 698 }));
    }

    [Test]
    public void ABoxSelectsTheGlyphsWhoseMiddlesItHolds()
    {
        var text = Text("BT /F1 10 Tf 100 700 Td (abcd) Tj 0 -20 Td (efgh) Tj ET");

        var area = new PdfRectangle(104, 670, 116, 710);

        Assert.That(text.GlyphsIn(area), Is.EqualTo(new[] { 1, 2, 5, 6 }));
        Assert.That(text.TextIn(area), Is.EqualTo("bc\nfg"));
    }

    [Test]
    public void RaisedAndLoweredGlyphsStayOnTheirLine()
    {
        Assert.That(Text("BT /F1 7 Tf 72 700 Td 4 Ts (1) Tj /F1 10 Tf 0 Ts (The note text) Tj ET").Text,
            Is.EqualTo("1The note text"));
        Assert.That(Text("BT /F1 10 Tf 72 700 Td (base) Tj 5 Ts (up) Tj -3 Ts (dn) Tj ET").Text, Is.EqualTo("baseupdn"));
    }

    [Test]
    public void AGlyphOfNoAdvanceInTurnedTextStaysOnTheLine()
    {
        var text = Text("BT /F1 10 Tf 0 1 -1 0 300 300 Tm (ab\\037cd) Tj ET");

        Assert.That(text.Lines, Has.Count.EqualTo(1));
        Assert.That(text.Glyphs, Has.Count.EqualTo(5));
    }

    [Test]
    public void ColumnsSideBySideAreLinesOfTheirOwn()
    {
        var text = Text("BT /F1 10 Tf 72 700 Td (Left 0) Tj 248 0 Td (Right 0) Tj -248 -14 Td (Left 1) Tj " +
                        "248 0 Td (Right 1) Tj ET");

        Assert.That(text.Text, Is.EqualTo("Left 0\nRight 0\nLeft 1\nRight 1"));
        Assert.That(text.Find("0 Right"), Has.Count.EqualTo(1), "a search may still run on from one to the next");
        Assert.That(text.Find("0 Right").Single().Quads, Has.Count.EqualTo(2), "but its boxes do not span the gutter");
    }

    [Test]
    public void AMatchEndingOnAnAddedSpaceDoesNotTakeTheNextGlyph()
    {
        var text = Text("BT /F1 10 Tf 0 0 Td (AB) Tj 15 0 Td (CD) Tj ET");

        Assert.That(text.Text, Is.EqualTo("AB CD"));
        Assert.That(text.Find("AB ").Select(m => (m.Start, m.End)), Is.EqualTo(new[] { (0, 2) }));
    }

    [Test]
    public void AMatchOfAnActualTextCoversItsWholeSpan()
    {
        var text = Text("BT /F1 10 Tf 0 0 Td /Span << /ActualText (cafe) >> BDC (cafX) Tj EMC ( more) Tj ET");

        Assert.That(text.Find("cafe").Select(m => (m.Start, m.End)), Is.EqualTo(new[] { (0, 4) }));
    }

    [Test, Timeout(20000)]
    public void FormsDrawingFormsManyTimesOverStopAtTheirBudget()
    {
        var forms = Enumerable.Range(0, 12).Select(level =>
            Encoding.Latin1.GetString(PdfFileBuilder.Stream(
                "/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /XObject << /N " + (8 + level) +
                " 0 R >> >>", Encoding.ASCII.GetBytes(level < 11 ? "/N Do /N Do /N Do /N Do" : "0 0 m")))).ToArray();

        var text = Text("/N Do", "/XObject << /N 7 0 R >>", forms);

        Assert.That(text.Glyphs, Is.Empty);
    }

    [Test, Timeout(20000)]
    public void ManyUnclosedMarkedSpansDoNotSlowEveryGlyph()
    {
        var content = new StringBuilder("BT /F1 10 Tf 0 0 Td ");
        for (var i = 0; i < 60000; i++)
        {
            content.Append("/P BMC ");
        }

        content.Append('(').Append(new string('a', 60000)).Append(") Tj ET");

        Assert.That(Text(content.ToString()).Glyphs, Has.Count.EqualTo(60000));
    }

    [Test]
    public void APageWithoutTextHasNone()
    {
        var text = Text("0 0 m 100 100 l S");

        Assert.That(text.Glyphs, Is.Empty);
        Assert.That(text.Text, Is.Empty);
        Assert.That(text.CaretAt(new PdfPoint(10, 10)), Is.EqualTo(0));
        Assert.That(text.Find("x"), Is.Empty);
        Assert.That(text.GetText(0, 5), Is.Empty);
    }
}
