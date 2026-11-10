using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfOptionalContentTests
{
    // Objects 3, 4 and 5 are the groups A, B and C; what follows is the test's own.
    private static PdfDocument Document(string properties, params string[] more)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R /OCProperties " + properties + " >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add("<< /Type /OCG /Name (A) >>");
        builder.Add("<< /Type /OCG /Name (B) /Usage << /Print << /PrintState /OFF >> /View << /ViewState /ON >> >> >>");
        builder.Add("<< /Type /OCG /Name (C) >>");
        foreach (var body in more)
        {
            builder.Add(body);
        }

        return PdfDocument.Open(builder.Build("/Root 1 0 R"));
    }

    private static bool Visible(PdfDocument document, int number, PdfOptionalContentUse use = PdfOptionalContentUse.View) =>
        PdfOptionalContent.Of(document, use).IsVisible(new PdfReference(new PdfObjectId(number, 0)));

    [Test]
    public void WithoutOptionalContentEverythingShows()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add("<< /Type /OCG /Name (A) >>");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var content = PdfOptionalContent.Of(document, PdfOptionalContentUse.View);
        Assert.That(content.IsPresent, Is.False);
        Assert.That(content.IsVisible(new PdfReference(new PdfObjectId(3, 0))), Is.True);
        Assert.That(content.IsVisible(null), Is.True);
    }

    [Test]
    public void TheDefaultConfigurationTurnsGroupsOff()
    {
        var document = Document("<< /OCGs [3 0 R 4 0 R 5 0 R] /D << /OFF [4 0 R] >> >>");
        Assert.That(Visible(document, 3), Is.True);
        Assert.That(Visible(document, 4), Is.False);
        Assert.That(Visible(document, 5), Is.True);
    }

    [Test]
    public void ABaseStateOfOffLeavesOnlyTheListedOnesOn()
    {
        var document = Document("<< /OCGs [3 0 R 4 0 R 5 0 R] /D << /BaseState /OFF /ON [5 0 R] >> >>");
        Assert.That(Visible(document, 3), Is.False);
        Assert.That(Visible(document, 5), Is.True);
    }

    [Test]
    public void TheUsageAppliedForPrintHidesWhatTheScreenShows()
    {
        var document = Document("<< /OCGs [3 0 R 4 0 R 5 0 R] /D << /AS [" +
                                "<< /Event /Print /Category [/Print] /OCGs [4 0 R] >> " +
                                "<< /Event /View /Category [/View] /OCGs [4 0 R] >>] >> >>");
        Assert.That(Visible(document, 4, PdfOptionalContentUse.View), Is.True);
        Assert.That(Visible(document, 4, PdfOptionalContentUse.Print), Is.False);
    }

    [TestCase("/AnyOn", true)]
    [TestCase("/AllOn", false)]
    [TestCase("/AnyOff", true)]
    [TestCase("/AllOff", false)]
    public void AMembershipDictionaryAppliesItsPolicy(string policy, bool visible)
    {
        var document = Document("<< /OCGs [3 0 R 4 0 R 5 0 R] /D << /OFF [4 0 R] >> >>",
            "<< /Type /OCMD /OCGs [3 0 R 4 0 R] /P " + policy + " >>");
        Assert.That(Visible(document, 6), Is.EqualTo(visible));
    }

    [Test]
    public void AVisibilityExpressionOverridesThePolicy()
    {
        // B is off: Or(Not(B), And(A, B)) - Not(B) is true.
        var document = Document("<< /OCGs [3 0 R 4 0 R 5 0 R] /D << /OFF [4 0 R] >> >>",
            "<< /Type /OCMD /OCGs [4 0 R] /P /AllOn /VE [/Or [/Not 4 0 R] [/And 3 0 R 4 0 R]] >>",
            "<< /Type /OCMD /VE [/And 3 0 R [/Not [/Or 4 0 R 5 0 R]]] >>");
        Assert.That(Visible(document, 6), Is.True);
        Assert.That(Visible(document, 7), Is.False);
    }

    [Test]
    public void AnExpressionHoldingItselfEndsAtTheDepthLimit()
    {
        var document = Document("<< /OCGs [3 0 R] /D << /OFF [3 0 R] >> >>", "[/And 6 0 R 3 0 R]",
            "<< /Type /OCMD /VE 6 0 R >>");
        Assert.That(Visible(document, 7), Is.False);
    }
}
