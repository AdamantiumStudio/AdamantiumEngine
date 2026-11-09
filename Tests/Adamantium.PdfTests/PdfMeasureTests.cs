using System.Linq;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Measurement;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Measure, number format and viewport dictionaries (ISO 32000-2, 12.9): read, written, and the lengths and
/// areas they turn user space into.</summary>
public class PdfMeasureTests
{
    private static PdfDocument Document(string page)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] " + page + " >>");
        return PdfDocument.Open(builder.Build("/Root 1 0 R"));
    }

    private static PdfObject Parse(string text) =>
        new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(text))).ReadObject();

    private const string FeetAndInches =
        "<< /Type /Measure /Subtype /RL /R (1 in = 4 ft) /X [<< /U (ft) /C 0.0555556 >> << /U (in) /C 12 /D 4 /F /F >>] " +
        "/D [<< /U (ft) /C 1 >> << /U (in) /C 12 /D 4 /F /F >>] /A [<< /U (sq ft) /C 1 >>] >>";

    [Test]
    public void FeetTakeTheWholeAndInchesTheRemainder()
    {
        var document = Document("");
        var measure = PdfMeasure.From(Parse(FeetAndInches), document);

        Assert.That(measure.Ratio, Is.EqualTo("1 in = 4 ft"));
        Assert.That(measure.X.Select(x => x.Unit), Is.EqualTo(new[] { "ft", "in" }));
        Assert.That(measure.FormatDistance(90, 0), Is.EqualTo("5 ft 0 in"), "90 pt = 1.25 in = 5 ft");
        Assert.That(measure.FormatDistance(81, 0), Is.EqualTo("4 ft 6 in"));
        Assert.That(measure.FormatDistance(80.4375, 0), Is.EqualTo("4 ft 5 3/4 in"));
        Assert.That(measure.FormatArea(72 * 72), Is.EqualTo("16.00 sq ft"));
    }

    [Test]
    public void ACalibratedScaleMeasuresAndWritesBack()
    {
        var measure = PdfMeasure.Scaled("100 pt = 4.5 m", "m", 4.5 / 100, 2);

        Assert.That(measure.Length(60, 80), Is.EqualTo(4.5).Within(1e-12));
        Assert.That(measure.FormatDistance(60, 80), Is.EqualTo("4.50 m"));
        Assert.That(measure.FormatArea(100 * 100), Is.EqualTo("20.25 sq m"));

        var again = PdfMeasure.From(measure.ToDictionary(), Document(""));
        Assert.That(again.FormatDistance(60, 80), Is.EqualTo("4.50 m"));
        Assert.That(again.Ratio, Is.EqualTo("100 pt = 4.5 m"));
    }

    [Test]
    public void ThousandsDecimalsAndALabelFirstAreAsTheFormatSays()
    {
        var format = new PdfNumberFormat("$", 1)
        {
            Precision = 1000, ThousandsSeparator = " ", DecimalSeparator = ",", LabelFirst = true, PrefixSpacing = "",
        };

        Assert.That(PdfMeasure.Format(1234567.8915, [format]), Is.EqualTo("$1 234 567,892"));
        Assert.That(PdfMeasure.Format(-3.5, [new PdfNumberFormat("mm", 10) { Style = PdfNumberStyle.Round }]),
            Is.EqualTo("-35 mm"));
        Assert.That(PdfMeasure.Format(2.75, [new PdfNumberFormat("in", 1) { Style = PdfNumberStyle.Fraction,
            Precision = 8, FixedDenominator = true }]), Is.EqualTo("2 6/8 in"));
    }

    [Test]
    public void RoundingCarriesIntoTheLargerUnit()
    {
        PdfNumberFormat Feet() => new("ft", 1);
        PdfNumberFormat Inches(PdfNumberStyle style, int precision) => new("in", 12) { Style = style, Precision = precision };

        Assert.That(PdfMeasure.Format(0.9999, [Feet(), Inches(PdfNumberStyle.Decimal, 100)]), Is.EqualTo("1 ft 0.00 in"));
        Assert.That(PdfMeasure.Format(1.9999, [Feet(), Inches(PdfNumberStyle.Fraction, 8)]), Is.EqualTo("2 ft 0 in"));
        Assert.That(PdfMeasure.Format(0.5 / 12, [Feet(), Inches(PdfNumberStyle.Round, 1)]), Is.EqualTo("0 ft 1 in"),
            "half away from zero, as decimals round");
        Assert.That(PdfMeasure.Format(-0.5, [Feet(), Inches(PdfNumberStyle.Decimal, 100)]), Is.EqualTo("-0 ft 6.00 in"));
        Assert.That(new PdfNumberFormat("in", 1) { Style = PdfNumberStyle.Fraction, Precision = 2 }.Format(-0.5, false),
            Is.EqualTo("-0 1/2 in"));
        Assert.That(PdfMeasure.Format(-0.0001, [new PdfNumberFormat("m", 1)]), Is.EqualTo("0.00 m"), "no negative zero");
    }

    [Test]
    public void YUnitsOfTheirOwnAreBroughtToX()
    {
        var measure = new PdfMeasure("", [new PdfNumberFormat("m", 1)], [new PdfNumberFormat("m", 1)],
            [new PdfNumberFormat("sq m", 1)]) { Y = [new PdfNumberFormat("km", 0.001)], YToX = 1000 };

        Assert.That(measure.Length(3, 4), Is.EqualTo(5).Within(1e-12));
        Assert.That(measure.AreaOf(2), Is.EqualTo(2).Within(1e-12));
    }

    [Test]
    public void APagesViewportsAreReadAndTheLastHoldingAPointWins()
    {
        var document = Document("/VP [<< /Type /Viewport /BBox [0 0 600 800] /Name (sheet) /Measure " + FeetAndInches +
                                " >> << /Type /Viewport /BBox [100 100 300 300] /Name (detail) >> (junk)]");

        var viewports = document.Pages[0].Viewports;

        Assert.That(viewports.Select(v => v.Name), Is.EqualTo(new[] { "sheet", "detail" }));
        Assert.That(viewports[0].Measure.Ratio, Is.EqualTo("1 in = 4 ft"));
        Assert.That(viewports[1].Measure, Is.Null);
        Assert.That(PdfViewport.At(viewports, 200, 200).Name, Is.EqualTo("detail"));
        Assert.That(PdfViewport.At(viewports, 50, 50).Name, Is.EqualTo("sheet"));
        Assert.That(PdfViewport.At(viewports, 700, 50), Is.Null);
        var written = PdfViewport.ListFrom(new PdfArray([viewports[0].ToDictionary()]), document).Single();
        Assert.That(written.Measure.FormatDistance(81, 0), Is.EqualTo("4 ft 6 in"));
    }

    [Test]
    public void AGeospatialMeasureIsNotTakenForAScale()
    {
        Assert.That(PdfMeasure.From(Parse("<< /Type /Measure /Subtype /GEO >>"), Document("")), Is.Null);
        Assert.That(PdfMeasure.From(Parse("<< /Type /Measure /R (x) /X [<< /U (m) >>] >>"), Document("")), Is.Null);
    }
}
