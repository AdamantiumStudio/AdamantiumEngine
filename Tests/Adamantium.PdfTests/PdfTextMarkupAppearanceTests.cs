using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>What a text markup with no appearance of its own looks like - highlight, underline, strikeout and squiggle
/// over its <c>QuadPoints</c> in its <c>C</c> and <c>CA</c> - and how the areas read.</summary>
public class PdfTextMarkupAppearanceTests
{
    // One line of text, 20 points tall, from 100 to 300 over the baseline side at 500.
    private const string OneLine = "/QuadPoints [100 520 300 520 100 500 300 500]";

    private static PdfAnnotation Markup(string subtype, string entries, string rect = "100 500 300 520")
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [<< /Type /Annot /Subtype /" +
                    subtype + " /Rect [" + rect + "] " + entries + " >>] >>");
        return PdfDocument.Open(builder.Build("/Root 1 0 R")).Pages[0].Annotations.Single();
    }

    private static string Content(PdfAnnotation markup) =>
        Encoding.ASCII.GetString(markup.ImpliedAppearance.Data.Span);

    private static PdfDictionary State(PdfAnnotation markup)
    {
        var resources = (PdfDictionary)markup.ImpliedAppearance.Dictionary["Resources"];
        return (PdfDictionary)((PdfDictionary)resources["ExtGState"])["Markup"];
    }

    [Test]
    public void AHighlight_FillsEachArea_MultipliedIntoThePage_AtItsOpacity()
    {
        var highlight = Markup("Highlight", OneLine + " /C [1 1 0] /CA 0.5");

        Assert.Multiple(() =>
        {
            Assert.That(Content(highlight), Is.EqualTo("q /Markup gs 1 1 0 rg 0 0 m 200 0 l 200 20 l 0 20 l h f Q"));
            Assert.That(((PdfName)State(highlight)["BM"]).Value, Is.EqualTo("Multiply"));
            Assert.That(((PdfReal)State(highlight)["ca"]).Value, Is.EqualTo(0.5));
            Assert.That(((PdfReal)State(highlight)["CA"]).Value, Is.EqualTo(0.5));
        });
    }

    [Test]
    public void AnUnderline_RunsJustOverTheBaselineSide_AndAStrikeout_ThroughTheMiddleOfTheLetters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Content(Markup("Underline", OneLine + " /C [0 0.5 0]")),
                Is.EqualTo("q /Markup gs 0 0.5 0 RG 0 J 0 j [] 0 d 1.4 w 0 1.6 m 200 1.6 l S Q"));
            Assert.That(Content(Markup("StrikeOut", OneLine + " /C [1 0 0]")),
                Is.EqualTo("q /Markup gs 1 0 0 RG 0 J 0 j [] 0 d 1.4 w 0 8.4 m 200 8.4 l S Q"));
            Assert.That(State(Markup("Underline", OneLine))["BM"], Is.Null);
        });
    }

    [Test]
    public void ASquiggle_ZigzagsAlongTheBaselineSide_ToTheEndOfTheArea()
    {
        var content = Content(Markup("Squiggly", OneLine + " /C [0 0 1]"));

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.StartWith("q /Markup gs 0 0 1 RG 0 J 0 j [] 0 d 1.4 w 0 1.4 m 4 3.4 l 8 1.4 l "));
            Assert.That(content, Does.EndWith(" 200 1.4 l S Q"));
            Assert.That(content.Split(" l ").Length - 1, Is.EqualTo(50));
        });
    }

    [Test]
    public void ATurnedLine_IsMarkedAlongItsOwnBaseline()
    {
        // Text running up the page: the baseline side is the right one, at x 120.
        var underline = Markup("Underline", "/QuadPoints [100 500 100 700 120 500 120 700] /C [0]", "100 500 120 700");

        Assert.That(Content(underline), Is.EqualTo("q /Markup gs 0 G 0 J 0 j [] 0 d 1.4 w 18.4 0 m 18.4 200 l S Q"));
    }

    [Test]
    public void AHighlightWithoutAColor_IsYellow_AndItsFillOpacityIsItsOwnWhenGiven()
    {
        var highlight = Markup("Highlight", OneLine + " /CA 1 /ca 0.3");

        Assert.Multiple(() =>
        {
            Assert.That(Content(highlight), Does.StartWith("q /Markup gs 1 1 0 rg "));
            Assert.That(((PdfReal)State(highlight)["ca"]).Value, Is.EqualTo(0.3).Within(1e-9));
            Assert.That(((PdfReal)State(highlight)["CA"]).Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void AHighlightWhoseCornersComeInAnotherOrder_IsStillFilledWhole()
    {
        // Top left, top right, bottom right, bottom left: taken in order, a bow tie.
        var highlight = Markup("Highlight", "/QuadPoints [100 520 300 520 300 500 100 500] /C [1 1 0]");

        Assert.That(Content(highlight), Does.Contain("0 0 m 200 0 l 200 20 l 0 20 l h f"));
    }

    [Test]
    public void ASquiggleFarLongerThanItsBox_IsDrawnOnlyOverTheBox_InBoundedSteps()
    {
        // PDF numbers have no exponent.
        var content = Content(Markup("Squiggly", "/QuadPoints [0 20 1000000000 20 0 0 1000000000 0]", "0 0 100 20"));
        var flat = Content(Markup("Squiggly", "/QuadPoints [0 0.001 1000000 0.001 0 0 1000000 0]", "0 0 1000000 20"));

        // Far from the origin, where a step of 4 is less than a double's own step; and many lines sharing one budget.
        var far = Content(Markup("Squiggly",
            "/QuadPoints [-100000000000000000 20 100 20 -100000000000000000 0 100 0]", "0 0 100 20"));
        var many = new System.Text.StringBuilder("/QuadPoints [");
        for (var line = 0; line < 100; line++)
        {
            many.Append("0 0.001 1000000 0.001 0 0 1000000 0 ");
        }

        var shared = Content(Markup("Squiggly", many.Append(']').ToString(), "0 0 1000000 20"));

        Assert.Multiple(() =>
        {
            Assert.That(content.Split(" l ").Length - 1, Is.InRange(25, 27));
            Assert.That(flat.Split(" l ").Length - 1, Is.LessThanOrEqualTo(16384));
            Assert.That(far.Split(" l ").Length - 1, Is.LessThanOrEqualTo(16384));
            Assert.That(shared.Split(" l ").Length - 1, Is.LessThanOrEqualTo(16384));
        });
    }

    [Test]
    public void ASquiggleOutsideItsBox_DrawsNothing()
    {
        Assert.That(Markup("Squiggly", "/QuadPoints [400 20 600 20 400 0 600 0]", "0 0 100 20").ImpliedAppearance,
            Is.Null);
    }

    [Test]
    public void ALineOfNoHeight_DrawsNoMark()
    {
        Assert.That(Markup("Underline", "/QuadPoints [100 500 300 500 100 500 300 500]").ImpliedAppearance, Is.Null);
    }

    [Test]
    public void NoAreas_NoColor_OrAnAppearanceOfItsOwn_DrawsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Markup("Highlight", "/C [1 1 0]").ImpliedAppearance, Is.Null);
            Assert.That(Markup("Highlight", OneLine + " /C []").ImpliedAppearance, Is.Null);
            Assert.That(Markup("Highlight", "/QuadPoints [100 520 100 520 100 500 100 500]").ImpliedAppearance, Is.Null);
            Assert.That(Markup("Highlight", OneLine + " /AP << /N 4 0 R >>").ImpliedAppearance, Is.Null);
            Assert.That(Markup("Square", OneLine).ImpliedAppearance, Is.Null);
        });
    }

    [Test]
    public void TheAreas_ReadInTheOrderFilesWriteThem_AndTheOpacityIsWholeWithoutCA()
    {
        var markup = Markup("Highlight", "/QuadPoints [10 30 50 30 10 20 50 20 1 2 3 4 5 6 7]");

        Assert.Multiple(() =>
        {
            Assert.That(markup.QuadPoints, Has.Count.EqualTo(1));
            Assert.That(markup.QuadPoints[0].TopLeft, Is.EqualTo(new PdfPoint(10, 30)));
            Assert.That(markup.QuadPoints[0].BottomRight, Is.EqualTo(new PdfPoint(50, 20)));
            Assert.That(markup.Opacity, Is.EqualTo(1));
            Assert.That(Markup("Highlight", "/CA 7").Opacity, Is.EqualTo(1));
        });
    }

    [Test]
    public void TheBuilder_NamesABlendModeOnce_InTheFormsResources()
    {
        var form = new PdfContentBuilder().BlendMode(PdfBlendMode.Multiply).FillColor(1, 1, 0).Rectangle(0, 0, 10, 10)
            .Fill().BlendMode(PdfBlendMode.Multiply).BlendMode(PdfBlendMode.Screen)
            .ToFormXObject(new PdfRectangle(0, 0, 10, 10));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new PdfContentBuilder().BlendMode((PdfBlendMode)99));

        using var input = new ZLibStream(new MemoryStream(form.Data.ToArray()), CompressionMode.Decompress);
        using var reader = new StreamReader(input, Encoding.ASCII);
        var states = (PdfDictionary)((PdfDictionary)form.Dictionary["Resources"])["ExtGState"];

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadToEnd(), Does.StartWith("/BM0 gs\n").And.Contain("/BM0 gs\n/BM1 gs\n"));
            Assert.That(((PdfName)((PdfDictionary)states["BM0"])["BM"]).Value, Is.EqualTo("Multiply"));
            Assert.That(((PdfName)((PdfDictionary)states["BM1"])["BM"]).Value, Is.EqualTo("Screen"));
        });
    }
}
