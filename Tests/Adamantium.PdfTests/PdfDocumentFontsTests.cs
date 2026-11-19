using System.Linq;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>The fonts a document uses, wherever they are kept - page resources, a form (one that holds itself), a Type 3
/// font's own resources, an annotation's appearance - each once, with its pages; and the file's size.</summary>
public class PdfDocumentFontsTests
{
    private static byte[] FontsEverywhere()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 5 0 R >> /XObject << /X1 6 0 R >> " +
                    "/ExtGState << /G1 13 0 R >> >> /Annots [9 0 R] >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 5 0 R /T3 8 0 R >> >> >>");
        builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        builder.Add(PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1] " +
                                          "/Resources << /Font << /F2 7 0 R >> /XObject << /Self 6 0 R >> >>",
            Encoding.ASCII.GetBytes("BT /F2 1 Tf ET")));
        builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman >>");
        builder.Add("<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1 1] /FontMatrix [0.001 0 0 0.001 0 0] " +
                    "/CharProcs << >> /Encoding << /Differences [] >> /FirstChar 0 /LastChar 0 /Widths [0] " +
                    "/Resources << /Font << /F3 10 0 R >> >> >>");
        builder.Add("<< /Type /Annot /Subtype /FreeText /Rect [0 0 10 10] /AP << /N 11 0 R >> >>");
        builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>");
        builder.Add(PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] " +
                                          "/Resources << /Font << /F4 12 0 R >> >>",
            Encoding.ASCII.GetBytes("BT /F4 1 Tf ET")));
        builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>");
        builder.Add("<< /Type /ExtGState /Font [14 0 R 12] /SMask << /S /Luminosity /G 15 0 R >> >>");
        builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>");
        builder.Add(PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1] /Group << /S /Transparency >> " +
                                          "/Resources << /Font << /F5 16 0 R >> >>",
            Encoding.ASCII.GetBytes("BT /F5 1 Tf ET")));
        builder.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold >>");
        return builder.Build("/Root 1 0 R");
    }

    [Test]
    public void EveryFont_IsFoundOnce_WithThePagesUsingIt()
    {
        var bytes = FontsEverywhere();
        var document = PdfDocument.Open(bytes);

        var fonts = document.CollectFonts();
        string PagesOf(string name) => string.Join(",", fonts.Single(found => found.Font.Name == name).Pages);

        Assert.Multiple(() =>
        {
            Assert.That(fonts, Has.Count.EqualTo(7));
            Assert.That(fonts[0].Font.Name, Is.EqualTo("Helvetica"), "the page's own fonts come first");
            Assert.That(fonts.Select(found => found.Font.Name).ToList().IndexOf("ZapfDingbats"),
                Is.GreaterThan(fonts.Select(found => found.Font.Name).ToList().IndexOf("Times-Roman")),
                "the page's forms before its annotations");
            Assert.That(PagesOf("Helvetica"), Is.EqualTo("0,1"), "the font both pages use");
            Assert.That(PagesOf("Symbol"), Is.EqualTo("0"), "the one a graphics state sets");
            Assert.That(PagesOf("Courier-Bold"), Is.EqualTo("0"), "the one in a soft mask's group");
            Assert.That(PagesOf("Times-Roman"), Is.EqualTo("0"), "the one in the form that holds itself");
            Assert.That(PagesOf("ZapfDingbats"), Is.EqualTo("0"), "the one in the annotation's appearance");
            Assert.That(PagesOf("Courier"), Is.EqualTo("1"), "the one in the Type 3 font's own resources");
            Assert.That(fonts.Count(found => found.Font is PdfType3Font), Is.EqualTo(1));
            Assert.That(document.Pages[1].CollectFonts(), Has.Count.EqualTo(3));
            Assert.That(document.FileSize, Is.EqualTo(bytes.Length));
        });
    }

    [Test]
    public void AFontNotEmbedded_SaysSo_AndWhatStandsInForIt()
    {
        var document = PdfDocument.Open(FontsEverywhere());

        var helvetica = document.CollectFonts().Single(found => found.Font.Name == "Helvetica").Font;

        Assert.Multiple(() =>
        {
            Assert.That(helvetica.IsEmbedded, Is.False);
            Assert.That(helvetica.Substitute, Is.Not.Null.And.Not.Empty, "a system font stands in for it");
        });
    }
}
