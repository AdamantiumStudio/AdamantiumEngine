using System.Linq;
using System.Text;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfContentTests
{
    private static PdfContentOperation[] Read(string content) =>
        PdfContentReader.Read(Encoding.Latin1.GetBytes(content)).ToArray();

    [Test]
    public void OperatorsTakeTheOperandsBeforeThem()
    {
        var operations = Read("q 1 0 0 1 72 720 cm /F1 12 Tf [(A) -120 (B)] TJ Q % comment\n0.5 g");
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "q", "cm", "Tf", "TJ", "Q", "g" }));
        Assert.That(operations[1].Operands.Count, Is.EqualTo(6));
        Assert.That(operations[1].Number(5), Is.EqualTo(720));
        Assert.That(((PdfName)operations[2].Operands[0]).Value, Is.EqualTo("F1"));
        Assert.That(((PdfArray)operations[3].Operands[0]).Count, Is.EqualTo(3));
        Assert.That(operations[5].Number(0), Is.EqualTo(0.5));
    }

    [Test]
    public void MarkedContentTakesItsPropertiesDictionary()
    {
        var operations = Read("/Span << /ActualText (fi) /MCID 3 >> BDC EMC");
        Assert.That(operations[0].Operator, Is.EqualTo("BDC"));
        Assert.That(((PdfDictionary)operations[0].Operands[1])["MCID"], Is.TypeOf<PdfInteger>());
    }

    [Test]
    public void AnInlineImageWritesItsKeysOutAndKeepsItsBytes()
    {
        var operations = Read("q BI /W 2 /H 2 /CS /RGB /BPC 8 /F /AHx ID\n" +
                              "FF000000FF000000FFFFFFFF> EI Q");
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "q", "BI", "Q" }));
        var image = operations[1].InlineImage;
        Assert.That(((PdfInteger)image.Dictionary["Width"]).Value, Is.EqualTo(2));
        Assert.That(((PdfName)image.Dictionary["ColorSpace"]).Value, Is.EqualTo("DeviceRGB"));
        Assert.That(((PdfName)image.Dictionary["Filter"]).Value, Is.EqualTo("ASCIIHexDecode"));
        Assert.That(Encoding.ASCII.GetString(image.Data.Span), Is.EqualTo("FF000000FF000000FFFFFFFF>"));
    }

    [Test]
    public void AnUnfilteredInlineImageEndsByItsSizeEvenWithEiInsideIt()
    {
        var data = new byte[] { 0x20, (byte)'E', (byte)'I', 0x20, 0x41, 0x42 };
        var content = PdfFileBuilder.Concat(Encoding.ASCII.GetBytes("BI /W 6 /H 1 /CS /G /BPC 8 ID "), data,
            Encoding.ASCII.GetBytes(" EI 1 g"));
        var operations = PdfContentReader.Read(content).ToArray();
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "BI", "g" }));
        Assert.That(operations[0].InlineImage.Data.ToArray(), Is.EqualTo(data));
    }

    [Test]
    public void AFilteredInlineImageEndsAtTheFirstEiFollowedByText()
    {
        var binary = new byte[] { 0x01, 0x20, (byte)'E', (byte)'I', 0x20, 0xFF, 0xFE, 0x00, 0x7F };
        var content = PdfFileBuilder.Concat(Encoding.ASCII.GetBytes("BI /W 3 /H 3 /CS /G /BPC 8 /F /Fl ID "), binary,
            Encoding.ASCII.GetBytes("\nEI Q"));
        var operations = PdfContentReader.Read(content).ToArray();
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "BI", "Q" }));
        Assert.That(operations[0].InlineImage.Data.ToArray(), Is.EqualTo(binary));
    }

    [Test]
    public void GarbageNeverStopsTheReader()
    {
        var operations = Read("] >> } 1 2 ) re ( unterminated");
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "re" }));
        Assert.That(operations[0].Operands.Count, Is.EqualTo(2));
    }

    [Test]
    public void OperandsWithoutAnOperatorAreBounded()
    {
        var content = string.Concat(Enumerable.Repeat("1 ", 10_000)) + "m";
        var operations = Read(content);
        Assert.That(operations.Single().Operands.Count, Is.EqualTo(PdfContentReader.MaxOperands));
    }
}
