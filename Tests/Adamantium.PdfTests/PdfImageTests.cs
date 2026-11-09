using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg;
using Adamantium.Pdf;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Images;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfImageTests
{
    private static PdfImageBitmap Decode(string dictionary, byte[] data, params byte[][] more)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var image = builder.Add(PdfFileBuilder.Stream("/Type /XObject /Subtype /Image " + dictionary, data));
        foreach (var body in more)
        {
            builder.Add(body);
        }

        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var stream = (PdfStream)document.GetObject(image);
        return PdfImageDecoder.Decode(stream, new PdfResources(new PdfDictionary(), document), document);
    }

    private static byte[] Pixel(PdfImageBitmap bitmap, int x, int y) =>
        bitmap.Rgba.Skip((y * bitmap.Width + x) * 4).Take(4).ToArray();

    [Test]
    public void RgbSamplesComeOutAsTheyAre()
    {
        var bitmap = Decode("/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8", [255, 0, 0, 0, 128, 255]);
        Assert.That(Pixel(bitmap, 0, 0), Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(Pixel(bitmap, 1, 0), Is.EqualTo(new byte[] { 0, 128, 255, 255 }));
    }

    [Test]
    public void OneBitGrayFollowsItsDecodeAndRowsStartOnBytes()
    {
        var bitmap = Decode("/Width 3 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 1 /Decode [1 0]",
            [0b10100000, 0b01000000]);
        Assert.That(Pixel(bitmap, 0, 0)[0], Is.EqualTo(0));
        Assert.That(Pixel(bitmap, 1, 0)[0], Is.EqualTo(255));
        Assert.That(Pixel(bitmap, 2, 0)[0], Is.EqualTo(0));
        Assert.That(Pixel(bitmap, 1, 1)[0], Is.EqualTo(0));
        Assert.That(Pixel(bitmap, 0, 1)[0], Is.EqualTo(255));
    }

    [Test]
    public void IndexedFourBitSamplesLookUpTheirColors()
    {
        var bitmap = Decode("/Width 2 /Height 1 /ColorSpace [/Indexed /DeviceRGB 1 <0000FFFF0000>] " +
                            "/BitsPerComponent 4", [0x10]);
        Assert.That(Pixel(bitmap, 0, 0), Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(Pixel(bitmap, 1, 0), Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
    }

    [Test]
    public void SixteenBitSamplesAreReadBigEndian()
    {
        var bitmap = Decode("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 16", [0x80, 0x00]);
        Assert.That(Pixel(bitmap, 0, 0)[0], Is.EqualTo(128));
    }

    [Test]
    public void AnImageMaskPaintsWhereItsSamplesAreZero()
    {
        var bitmap = Decode("/Width 2 /Height 1 /ImageMask true", [0b01000000]);
        Assert.That(bitmap.IsStencil, Is.True);
        Assert.That(Pixel(bitmap, 0, 0)[3], Is.EqualTo(255));
        Assert.That(Pixel(bitmap, 1, 0)[3], Is.EqualTo(0));
    }

    [Test]
    public void ASoftMaskSetsAlphaAndIsScaledToTheImage()
    {
        var bitmap = Decode("/Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 4 0 R",
            [10, 20, 30, 40],
            PdfFileBuilder.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray " +
                                  "/BitsPerComponent 8", [128]));
        Assert.That(bitmap.Rgba.Where((_, i) => i % 4 == 3), Is.All.EqualTo(128));
    }

    [Test]
    public void AnExplicitMaskHidesWhereItsSamplesAreOne()
    {
        var bitmap = Decode("/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask 4 0 R", [10, 20],
            PdfFileBuilder.Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ImageMask true", [0b01000000]));
        Assert.That(Pixel(bitmap, 0, 0)[3], Is.EqualTo(255));
        Assert.That(Pixel(bitmap, 1, 0)[3], Is.EqualTo(0));
    }

    [Test]
    public void AColorKeyMaskHidesSamplesInItsRanges()
    {
        var bitmap = Decode("/Width 3 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask [100 200]",
            [50, 150, 250]);
        Assert.That(bitmap.Rgba.Where((_, i) => i % 4 == 3), Is.EqualTo(new byte[] { 255, 0, 255 }));
    }

    [Test]
    public void AJpegImageDecodesThroughTheEnginesDecoder()
    {
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;
            pixels[i + 1] = 40;
            pixels[i + 2] = 40;
            pixels[i + 3] = 255;
        }

        using var jpeg = new MemoryStream();
        JpegEncoder.Encode(new JpegImage(16, 16, SurfaceFormat.R8G8B8A8.UNorm, pixels), jpeg, 95);
        var bitmap = Decode("/Width 16 /Height 16 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",
            jpeg.ToArray());
        var center = Pixel(bitmap, 8, 8);
        Assert.That(center[0], Is.EqualTo(200).Within(4));
        Assert.That(center[1], Is.EqualTo(40).Within(4));
        Assert.That(center[3], Is.EqualTo(255));
    }

    [Test]
    public void AnInlineImageDecodesFromItsContent()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var operation = PdfContentReader.Read(Encoding.ASCII.GetBytes(
            "BI /W 1 /H 1 /CS /RGB /BPC 8 /F /AHx ID 00FF00> EI")).Single();
        var bitmap = PdfImageDecoder.Decode(operation.InlineImage, new PdfResources(new PdfDictionary(), document),
            document);
        Assert.That(Pixel(bitmap, 0, 0), Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
    }

    [Test]
    public void ImagesThatCannotBeDrawnAreRefused()
    {
        Assert.Throws<PdfException>(() => Decode("/Width 0 /Height 5 /ColorSpace /DeviceGray /BitsPerComponent 8", []));
        Assert.Throws<PdfException>(() => Decode("/Width 70000 /Height 70000 /ColorSpace /DeviceGray", []));
        Assert.Throws<PdfException>(() => Decode("/Width 1 /Height 1 /Filter /JBIG2Decode", [0]));
    }

    [Test]
    public void SamplesCutShortReadAsZero()
    {
        var bitmap = Decode("/Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8", [255, 255, 255]);
        Assert.That(Pixel(bitmap, 0, 0), Is.EqualTo(new byte[] { 255, 255, 255, 255 }));
        Assert.That(Pixel(bitmap, 1, 1), Is.EqualTo(new byte[] { 0, 0, 0, 255 }));
    }
}
