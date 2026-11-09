using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg;
using Adamantium.Pdf;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Images;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>What the review of content, functions, color spaces and images broke, each kept broken no more.</summary>
public class PdfContentHostileTests
{
    private static (PdfDocument Document, int First) Document(params byte[][] objects)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var first = builder.Next;
        foreach (var body in objects)
        {
            builder.Add(body);
        }

        return (PdfDocument.Open(builder.Build("/Root 1 0 R")), first);
    }

    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text);

    private static PdfImageBitmap Image(string dictionary, byte[] data, params byte[][] more)
    {
        var all = new[] { PdfFileBuilder.Stream("/Type /XObject /Subtype /Image " + dictionary, data) }.Concat(more)
            .ToArray();
        var (document, first) = Document(all);
        return PdfImageDecoder.Decode((PdfStream)document.GetObject(first),
            new PdfResources(new PdfDictionary(), document), document);
    }

    private static double Evaluate(PdfFunction function, double x)
    {
        var output = new double[function.Outputs];
        function.Evaluate([x], output);
        return output[0];
    }

    [Test]
    public void AFunctionReferencedManyTimesIsReadOnce()
    {
        var objects = new byte[17][];
        for (var level = 0; level < 16; level++)
        {
            var next = 3 + level + 1;
            var children = string.Join(" ", Enumerable.Repeat(next + " 0 R", 10));
            var bounds = string.Join(" ", Enumerable.Range(1, 9).Select(i => (i / 10.0).ToString("0.0",
                System.Globalization.CultureInfo.InvariantCulture)));
            objects[level] = Text($"<< /FunctionType 3 /Domain [0 1] /Functions [{children}] /Bounds [{bounds}] " +
                                  "/Encode [0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 1 0 1] >>");
        }

        objects[16] = Text("<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>");
        var (document, first) = Document(objects);
        var watch = Stopwatch.StartNew();
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000));
        Assert.That(function, Is.Not.Null);
        Assert.That(Evaluate(function, 0.05), Is.InRange(0.0, 1.0));
    }

    [Test]
    public void PostScriptIntegerDivisionNeverOverflows()
    {
        var (document, first) = Document(PdfFileBuilder.Stream("/FunctionType 4 /Domain [0 1] /Range [-1e30 1e30]",
            Text("{ pop -9223372036854775808.0 -1 idiv -9223372036854775808.0 -1 mod add 7 -2 mod add }")));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.DoesNotThrow(() => Evaluate(function, 0));
    }

    [Test]
    public void PostScriptKeepsBooleansApartFromIntegers()
    {
        var (document, first) = Document(PdfFileBuilder.Stream("/FunctionType 4 /Domain [0 1] /Range [-9 9 -9 9 0 1]",
            Text("{ pop 1 not 0 not true not }")));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        var output = new double[3];
        function.Evaluate([0.0], output);
        Assert.That(output, Is.EqualTo(new double[] { -2, -1, 0 }));
    }

    [Test]
    public void PostScriptPopsAndAddsAtAFullStack()
    {
        var program = "{ pop " + string.Concat(Enumerable.Repeat("1 ", 99)) + "add pop " +
                      string.Concat(Enumerable.Repeat("pop ", 96)) + "}";
        var (document, first) = Document(PdfFileBuilder.Stream("/FunctionType 4 /Domain [0 1] /Range [0 9]",
            Text(program)));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0), Is.EqualTo(1));
    }

    [Test]
    public void ASampledFunctionPastTheLimitIsRefused()
    {
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [4096 4096] /BitsPerSample 1 /Filter /FlateDecode",
            PdfFileBuilder.Flate(new byte[4096 * 4096 / 8])));
        Assert.That(PdfFunctions.Create(document.GetObject(first), document), Is.Null);
    }

    [Test]
    public void AStrayBracketDoesNotSwallowTheContent()
    {
        var operations = PdfContentReader.Read(Text("[ (a) Tj BT /F1 12 Tf (x) Tj ET")).ToArray();
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "Tj", "BT", "Tf", "Tj", "ET" }));
    }

    [Test]
    public void AnInlineImageKeyWithoutAValueStopsAtId()
    {
        var operations = PdfContentReader.Read(Text("BI /W ID \u0001\u0002 EI Q")).ToArray();
        Assert.That(operations.Select(o => o.Operator), Is.EqualTo(new[] { "BI", "Q" }));
    }

    [Test]
    public void AnImageInAPatternSpaceReadsAsGray()
    {
        var bitmap = Image("/Width 1 /Height 1 /ColorSpace [/Pattern] /BitsPerComponent 8", [128]);
        Assert.That(bitmap.Rgba[0], Is.EqualTo(128));
    }

    [Test]
    public void JpegThatCannotBeDecodedIsAPdfException()
    {
        byte[] sixteenBit = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x10, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
            0xFF, 0xD9];
        Assert.Throws<PdfException>(() => Image("/Width 1 /Height 1 /Filter /DCTDecode", sixteenBit));
    }

    [Test]
    public void AHugeJpegFrameIsRefusedBeforeItIsDecoded()
    {
        byte[] huge = [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x2E, 0xE0, 0x2E, 0xE0, 0x01, 0x01, 0x11, 0x00,
            0xFF, 0xD9];
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<PdfException>(() => Image("/Width 12000 /Height 12000 /Filter /DCTDecode", huge));
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, Is.LessThan(16L << 20),
            "a 12000 x 12000 frame decoded would take over 100 MB");
    }

    [Test]
    public void AJpegFollowsTheImagesDecodeArray()
    {
        var pixels = new byte[8 * 8 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 40;
            pixels[i + 3] = 255;
        }

        using var jpeg = new MemoryStream();
        JpegEncoder.Encode(new JpegImage(8, 8, SurfaceFormat.R8G8B8A8.UNorm, pixels), jpeg, 95);
        var bitmap = Image("/Width 8 /Height 8 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Decode [1 0 1 0 1 0] " +
                           "/Filter /DCTDecode", jpeg.ToArray());
        Assert.That(bitmap.Rgba[0], Is.EqualTo(215).Within(4));
    }

    [Test]
    public void ATintTransformRunsOnAGridNotOncePerPixel()
    {
        var program = "{ " + string.Concat(Enumerable.Repeat("dup pop ", 2000)) + "pop 0.5 }";
        var watch = Stopwatch.StartNew();
        var bitmap = Image("/Width 1024 /Height 1024 /ColorSpace [/DeviceN [/A /B] /DeviceGray 4 0 R] " +
                           "/BitsPerComponent 8 /Filter /FlateDecode",
            PdfFileBuilder.Flate(Enumerable.Range(0, 1024 * 1024 * 2).Select(i => (byte)i).ToArray()),
            PdfFileBuilder.Stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1]", Text(program)));
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(5000));
        Assert.That(bitmap.Rgba[0], Is.EqualTo(128));
    }

    [Test]
    public void RowsPastTheDataCostNoColorWork()
    {
        var watch = Stopwatch.StartNew();
        var bitmap = Image("/Width 4096 /Height 4096 /ColorSpace /DeviceGray /BitsPerComponent 8", [255]);
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000));
        Assert.That(bitmap.Rgba[0], Is.EqualTo(255));
        Assert.That(bitmap.Rgba[^4], Is.EqualTo(0));
    }

    [Test]
    public void AnExplicitMaskIsAStencilEvenWithoutImageMask()
    {
        var bitmap = Image("/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask 4 0 R", [10, 20],
            PdfFileBuilder.Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 1",
                [0b01000000]));
        Assert.That(bitmap.Rgba[3], Is.EqualTo(255));
        Assert.That(bitmap.Rgba[7], Is.EqualTo(0));
    }

    [Test]
    public void AnImageInSeparationNonePaintsNothing()
    {
        var bitmap = Image("/Width 1 /Height 1 /ColorSpace [/Separation /None /DeviceGray 4 0 R] /BitsPerComponent 8",
            [255], Text("<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>"));
        Assert.That(bitmap.Rgba[3], Is.EqualTo(0));
    }
}
