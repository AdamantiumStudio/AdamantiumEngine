using System.Linq;
using System.Text;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Navigation;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>What a link with no appearance of its own looks like - its border from <c>BS</c>, <c>Border</c> and
/// <c>C</c> - and how it answers a press (<c>H</c>).</summary>
public class PdfLinkAppearanceTests
{
    private static PdfAnnotation Link(string entries, params byte[][] more)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [<< /Type /Annot /Subtype /Link " +
                    "/Rect [60 114 520 138] /Dest [3 0 R /Fit] " + entries + " >>] >>");
        foreach (var body in more)
        {
            builder.Add(body);
        }

        return PdfDocument.Open(builder.Build("/Root 1 0 R")).Pages[0].Annotations.Single();
    }

    private static string Content(PdfAnnotation link) => Encoding.ASCII.GetString(link.ImpliedAppearance.Data.Span);

    [Test]
    public void AThinColoredBorder_IsTheRectangleStrokedInItsColor_InsideItsBox()
    {
        var link = Link("/Border [0 0 0.5] /C [0.2 0.4 0.9]");

        Assert.That(Content(link), Is.EqualTo("q 0.2 0.4 0.9 RG 0.5 w 0.25 0.25 459.5 23.5 re S Q"));
        var box = (PdfArray)link.ImpliedAppearance.Dictionary["BBox"];
        Assert.That(box.OfType<PdfReal>().Select(number => number.Value), Is.EqualTo(new[] { 0, 0, 460, 24.0 }));
    }

    [Test]
    public void ALinkThatSaysNothing_HasAOnePointBlackBorder()
    {
        Assert.That(Content(Link("")), Is.EqualTo("q 0 G 1 w 0.5 0.5 459 23 re S Q"));
    }

    [Test]
    public void NoWidth_NoColor_OrAnAppearanceOfItsOwn_DrawsNoBorder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Link("/Border [0 0 0]").ImpliedAppearance, Is.Null);
            Assert.That(Link("/C []").ImpliedAppearance, Is.Null);
            Assert.That(Link("/AP << /N 4 0 R >>", PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]",
                System.Array.Empty<byte>())).ImpliedAppearance, Is.Null);
        });
    }

    [Test]
    public void TheBorderStyle_WinsOverTheOldBorder_AndDashesOrUnderlines()
    {
        var dashed = Link("/Border [0 0 0] /BS << /W 2 /S /D /D [4 2] >> /C [1 0 0]");
        var underlined = Link("/BS << /W 2 /S /U >> /C [0.5]");

        Assert.Multiple(() =>
        {
            Assert.That(dashed.Border.Style, Is.EqualTo(PdfBorderStyle.Dashed));
            Assert.That(Content(dashed), Is.EqualTo("q 1 0 0 RG 2 w [4 2 ] 0 d 1 1 458 22 re S Q"));
            Assert.That(Content(underlined), Is.EqualTo("q 0.5 G 2 w 0 1 m 460 1 l S Q"));
        });
    }

    [Test]
    public void RoundedCorners_FromTheOldBorder_AreCurves()
    {
        var link = Link("/Border [4 4 1] /C [0 0 0 1]");

        Assert.Multiple(() =>
        {
            Assert.That(link.Border.CornerX, Is.EqualTo(4));
            Assert.That(Content(link), Does.StartWith("q 0 0 0 1 K 1 w ").And.Contain(" c ").And.EndWith("h S Q"));
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void InAnEncryptedFile_TheBuiltBorder_IsReadPlain(int revision)
    {
        var encryption = new PdfTestEncryption(revision, string.Empty, "owner");
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [<< /Type /Annot /Subtype /Link " +
                    "/Rect [0 0 20 10] /Border [0 0 1] /C [1 0 0] >>] >>");
        builder.Add(encryption.Dictionary);
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R /Encrypt 4 0 R /ID " + encryption.IdArray));
        var link = document.Pages[0].Annotations.Single();

        var content = Encoding.ASCII.GetString(document.Decode(link.ImpliedAppearance).Data.Span);

        Assert.That(content, Is.EqualTo("q 1 0 0 RG 1 w 0.5 0.5 19 9 re S Q"));
    }

    [Test]
    public void AnOldBorderWithAnEmptyOrBrokenDash_IsSolid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Link("/Border [0 0 1 []]").Border.Style, Is.EqualTo(PdfBorderStyle.Solid));
            Assert.That(Link("/Border [0 0 1 0]").Border.Style, Is.EqualTo(PdfBorderStyle.Solid));
            Assert.That(Link("/Border [0 0 1 [2 1]]").Border.Dash, Is.EqualTo(new[] { 2.0, 1 }));
        });
    }

    [Test]
    public void ALinkWhoseAppearanceNamesNoState_DrawsNoBorderOfItsOwn()
    {
        Assert.That(Link("/AP << /N << /On 4 0 R >> >>").ImpliedAppearance, Is.Null);
    }

    [Test]
    public void ALinkOverTwoLines_IsBorderedLineByLine()
    {
        var link = Link("/QuadPoints [60 138 300 138 60 126 300 126 60 126 200 126 60 114 200 114] /C [0]");

        Assert.That(Content(link), Is.EqualTo("q 0 G 1 w 0.5 12.5 m 239.5 12.5 l 239.5 23.5 l 0.5 23.5 l h " +
                                              "0.5 0.5 m 139.5 0.5 l 139.5 11.5 l 0.5 11.5 l h S Q"));
    }

    [Test]
    public void APress_IsAnsweredAsTheLinkSays_InvertedByDefault()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [" +
                    "<< /Subtype /Link /Rect [0 0 10 10] /Dest [3 0 R /Fit] >> " +
                    "<< /Subtype /Link /Rect [0 0 10 10] /Dest [3 0 R /Fit] /H /N >> " +
                    "<< /Subtype /Link /Rect [0 0 10 10] /Dest [3 0 R /Fit] /H /O >> " +
                    "<< /Subtype /Link /Rect [0 0 10 10] /Dest [3 0 R /Fit] /H /P >>] >>");

        var links = PdfDocument.Open(builder.Build("/Root 1 0 R")).Pages[0].Links;

        Assert.That(links.Select(link => link.Highlight), Is.EqualTo(new[]
        {
            PdfLinkHighlight.Invert, PdfLinkHighlight.None, PdfLinkHighlight.Outline, PdfLinkHighlight.Push,
        }));
    }
}
