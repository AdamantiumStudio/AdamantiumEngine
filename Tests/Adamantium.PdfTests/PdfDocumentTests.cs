using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfDocumentTests
{
    private static PdfFileBuilder TwoPages()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 595 842] /Rotate 90 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /Contents 5 0 R /Resources << /Font << >> >> >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Rotate -90 /Contents [5 0 R 6 0 R] >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, Encoding.ASCII.GetBytes("BT /F1 12 Tf (one) Tj ET")));
        builder.Add(PdfFileBuilder.Stream("/Filter /FlateDecode",
            PdfFileBuilder.Flate(Encoding.ASCII.GetBytes("0 0 m 10 10 l S"))));
        builder.Add("<< /Title (Two pages) /Author <FEFF0410> >>");
        return builder;
    }

    [Test]
    public void ATableFileOpensWithItsPagesAndInheritedAttributes()
    {
        var document = PdfDocument.Open(TwoPages().Build("/Root 1 0 R /Info 7 0 R"));
        Assert.That(document.IsRepaired, Is.False);
        Assert.That(document.Version, Is.EqualTo("1.7"));
        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Two pages"));
        Assert.That(document.GetInfoText("Author"), Is.EqualTo(((char)0x0410).ToString()));
        var first = document.Pages[0];
        Assert.That(first.MediaBox, Is.EqualTo(new PdfRectangle(0, 0, 595, 842)));
        Assert.That(first.CropBox, Is.EqualTo(first.MediaBox));
        Assert.That(first.Rotation, Is.EqualTo(90));
        Assert.That(first.Width, Is.EqualTo(842));
        Assert.That(first.Height, Is.EqualTo(595));
        Assert.That(first.Resources.ContainsKey("Font"), Is.True);
        var second = document.Pages[1];
        Assert.That(second.MediaBox, Is.EqualTo(new PdfRectangle(0, 0, 200, 100)));
        Assert.That(second.Rotation, Is.EqualTo(270));
        Assert.That(Encoding.ASCII.GetString(second.GetContents().Span),
            Is.EqualTo("BT /F1 12 Tf (one) Tj ET\n0 0 m 10 10 l S"));
    }

    [Test]
    public void AStreamFileWithAnObjectStreamOpens()
    {
        var file = TwoPages().BuildWithStream("/Root 1 0 R /Info 7 0 R", new HashSet<int> { 1, 2, 3, 4, 7 });
        var document = PdfDocument.Open(file);
        Assert.That(document.IsRepaired, Is.False);
        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Two pages"));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT /F1 12 Tf (one) Tj ET"));
    }

    [Test]
    public void AnIncrementalUpdateReplacesTheObjectsItRewrites()
    {
        var original = TwoPages().Build("/Root 1 0 R /Info 7 0 R");
        var updated = PdfFileBuilder.Update(original, new Dictionary<int, string>
        {
            [7] = "<< /Title (Updated) >>",
            [8] = "<< /Type /Page /Parent 2 0 R >>",
        }, "/Size 9 /Root 1 0 R /Info 7 0 R", PdfFileBuilder.StartXref(original));
        var document = PdfDocument.Open(updated);
        Assert.That(document.IsRepaired, Is.False);
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Updated"));
        Assert.That(document.PageCount, Is.EqualTo(2));
    }

    [Test]
    public void APrevThatLoopsBackDoesNotHang()
    {
        var original = TwoPages().Build("/Root 1 0 R");
        var start = PdfFileBuilder.StartXref(original);
        var looped = PdfFileBuilder.Update(original, new Dictionary<int, string> { [7] = "<< /Title (Loop) >>" },
            "/Size 8 /Root 1 0 R /Info 7 0 R", start);
        var text = Encoding.Latin1.GetString(looped).Replace("/Prev " + start, "/Prev " + PdfFileBuilder.StartXref(looped));
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));
        Assert.That(document.PageCount, Is.EqualTo(2));
    }

    [Test]
    public void AFileWithoutAUsableCrossReferenceIsRepaired()
    {
        var file = TwoPages().Build("/Root 1 0 R /Info 7 0 R");
        var text = Encoding.Latin1.GetString(file);
        var broken = Encoding.Latin1.GetBytes(text[..text.LastIndexOf("startxref")] + "startxref\n999999\n%%EOF\n");
        var document = PdfDocument.Open(broken);
        Assert.That(document.IsRepaired, Is.True);
        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Two pages"));
    }

    [Test]
    public void ARepairedFileWithoutATrailerFindsItsCatalog()
    {
        var file = TwoPages().Build("/Root 1 0 R");
        var text = Encoding.Latin1.GetString(file);
        var noTrailer = Encoding.Latin1.GetBytes(text[..text.IndexOf("xref\n0 ")]);
        var document = PdfDocument.Open(noTrailer);
        Assert.That(document.IsRepaired, Is.True);
        Assert.That(document.PageCount, Is.EqualTo(2));
    }

    [Test]
    public void ARepairedStreamFileFindsTheObjectsInsideObjectStreams()
    {
        var file = TwoPages().BuildWithStream("/Root 1 0 R /Info 7 0 R", new HashSet<int> { 1, 2, 3, 4, 7 });
        var text = Encoding.Latin1.GetString(file);
        var broken = Encoding.Latin1.GetBytes(text[..text.LastIndexOf("startxref")] + "startxref\n5\n%%EOF\n");
        var document = PdfDocument.Open(broken);
        Assert.That(document.IsRepaired, Is.True);
        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Two pages"));
    }

    [Test]
    public void RepairTakesNoObjectHeaderInsideAStream()
    {
        var builder = TwoPages();
        builder.Set(5, PdfFileBuilder.Stream(string.Empty, Encoding.ASCII.GetBytes("7 0 obj << /Title (Fake) >> endobj")));
        var file = builder.Build("/Root 1 0 R /Info 7 0 R");
        var text = Encoding.Latin1.GetString(file);
        var objectSeven = text.IndexOf("\n7 0 obj") + 1;
        var reordered = text[..text.IndexOf("5 0 obj")] + text[objectSeven..text.IndexOf("xref\n0 ")] +
                        text[text.IndexOf("5 0 obj")..objectSeven] + "trailer << /Root 1 0 R /Info 7 0 R >>\n";
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(reordered));
        Assert.That(document.IsRepaired, Is.True);
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Two pages"));
    }

    [Test]
    public void OneWrongOffsetIsFoundByScanning()
    {
        var file = TwoPages().Build("/Root 1 0 R /Info 7 0 R");
        var text = Encoding.Latin1.GetString(file);
        var table = text.IndexOf("xref\n0 ");
        var seventh = text.IndexOf('\n', text.IndexOf("0000000000 65535 f", table)) + 1 + 6 * 20;
        var wrong = text[..seventh] + "0000000001" + text[(seventh + 10)..];
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(wrong));
        Assert.That(document.IsRepaired, Is.False);
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Two pages"));
    }

    [Test]
    public void BytesBeforeTheHeaderShiftTheOffsets()
    {
        var builder = TwoPages();
        builder.Prefix = Encoding.ASCII.GetBytes("garbage written before the header\n");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        Assert.That(document.PageCount, Is.EqualTo(2));
    }

    [Test]
    public void AWrongStreamLengthIsCorrectedByEndstream()
    {
        var builder = TwoPages();
        builder.Set(5, "<< /Length 3 >>\nstream\nBT (long) Tj ET\nendstream");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (long) Tj ET"));
    }

    [Test]
    public void AnIndirectStreamLengthIsResolved()
    {
        var builder = TwoPages();
        builder.Set(5, "<< /Length 8 0 R >>\nstream\nq Q endstream inside\nendstream");
        builder.Set(8, "20");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("q Q endstream inside"));
    }

    [Test]
    public void APageTreeThatLoopsEnds()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R 2 0 R 4 0 R] >>");
        builder.Add("<< /Type /Page /Parent 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [2 0 R 3 0 R 5 0 R] >>");
        builder.Add("<< /Type /Page /CropBox [-100 -100 5000 5000] >>");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.Pages[1].CropBox, Is.EqualTo(new PdfRectangle(0, 0, 612, 792)));
    }

    [Test]
    public void AReferenceToAMissingObjectIsNull()
    {
        var document = PdfDocument.Open(TwoPages().Build("/Root 1 0 R /Info 99 0 R"));
        Assert.That(document.Info, Is.Null);
        Assert.That(document.GetObject(99), Is.SameAs(PdfNull.Value));
    }

    [Test]
    public void PageLabelsNumberInTheirStyles()
    {
        var builder = TwoPages();
        builder.Set(1, "<< /Type /Catalog /Pages 2 0 R /PageLabels << /Nums [0 << /S /r >> 1 8 0 R] >> >>");
        builder.Set(8, "<< /S /A /P (Annex-) /St 27 >>");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        Assert.That(document.PageLabels.GetLabel(0), Is.EqualTo("i"));
        Assert.That(document.PageLabels.GetLabel(1), Is.EqualTo("Annex-AA"));
        Assert.That(document.PageLabels.GetLabel(2), Is.EqualTo("Annex-BB"));
    }

    [Test]
    public void ManyStreamsWithoutEndstreamStayLinear()
    {
        static long Time(int count)
        {
            var builder = new PdfFileBuilder();
            builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
            builder.Add("<< /Type /Pages /Kids [] >>");
            for (var i = 0; i < count; i++)
            {
                builder.Add("<< /Length 999999 >>\nstream\nxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
            }

            var file = builder.Build("/Root 1 0 R");
            var watch = Stopwatch.StartNew();
            var document = PdfDocument.Open(file);
            for (var number = 3; number < count + 3; number++)
            {
                document.GetObject(number);
            }

            return watch.ElapsedMilliseconds;
        }

        Time(1000);
        var small = Time(5_000);
        var large = Time(10_000);
        Assert.That(large, Is.LessThan(small * 4 + 200));
    }
}
