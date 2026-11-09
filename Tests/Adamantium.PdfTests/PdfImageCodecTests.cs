using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.ImagingTests;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Images;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Images whose filter is a codec of the engine's imaging: fax, JBIG2, JPEG 2000.</summary>
public class PdfImageCodecTests
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

    private static byte[] Grays(PdfImageBitmap bitmap) => bitmap.Rgba.Where((_, i) => i % 4 == 0).ToArray();

    /// <summary>Each row: white 2 (0111), black 3 (10), white 3 (1000).</summary>
    [Test]
    public void OneDimensionalFaxRowsDecode()
    {
        var bitmap = Decode("/Width 8 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 1 " +
                            "/Filter /CCITTFaxDecode /DecodeParms << /K 0 /Columns 8 >>", [0x7A, 0x1E, 0x80]);

        byte[] row = [255, 255, 0, 0, 0, 255, 255, 255];
        Assert.That(Grays(bitmap), Is.EqualTo(row.Concat(row)));
    }

    /// <summary>Row 1: horizontal mode (001, white 2, black 3), then V0; row 2: three V0 codes.</summary>
    [Test]
    public void GroupFourFaxRowsDecodeAgainstTheRowAbove()
    {
        var bitmap = Decode("/Width 8 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 1 " +
                            "/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 8 >>", [0x2F, 0x78]);

        byte[] row = [255, 255, 0, 0, 0, 255, 255, 255];
        Assert.That(Grays(bitmap), Is.EqualTo(row.Concat(row)));
    }

    [Test]
    public void BlackIsOneTurnsTheBitsOver()
    {
        var bitmap = Decode("/Width 8 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 1 " +
                            "/Filter /CCITTFaxDecode /DecodeParms << /K 0 /Columns 8 /BlackIs1 true >>", [0x7A, 0x00]);

        Assert.That(Grays(bitmap), Is.EqualTo(new byte[] { 0, 0, 255, 255, 255, 0, 0, 0 }));
    }

    [Test]
    public void FaxImageMaskPaintsItsBlack()
    {
        var bitmap = Decode("/Width 8 /Height 1 /ImageMask true " +
                            "/Filter /CCITTFaxDecode /DecodeParms << /K 0 /Columns 8 >>", [0x7A, 0x00]);

        Assert.That(bitmap.Rgba.Where((_, i) => i % 4 == 3), Is.EqualTo(new byte[] { 0, 0, 255, 255, 255, 0, 0, 0 }));
    }

    [Test]
    public void Jbig2ImageReadsItsGlobals()
    {
        var symbol = new byte[3, 4];
        for (var x = 0; x < 4; x++)
        {
            symbol[1, x] = 1;
        }

        List<byte[,]> symbols = [symbol];
        var globals = Jbig2Writer.Segment(0, 0, Jbig2Writer.SymbolDictionary(symbols));
        byte[] page =
        [
            .. Jbig2Writer.Segment(1, 48, Jbig2Writer.PageInformation(8, 4)),
            .. Jbig2Writer.Segment(2, 6, Jbig2Writer.TextRegion(8, 4, 1, [(0, 2, 1)], symbols), 0),
        ];
        var bitmap = Decode("/Width 8 /Height 4 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode " +
                            "/DecodeParms << /JBIG2Globals 4 0 R >>", page, PdfFileBuilder.Stream("", globals));

        var grays = Grays(bitmap);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var black = y == 2 && x is >= 2 and < 6;
                Assert.That(grays[y * 8 + x], Is.EqualTo(black ? 0 : 255), $"({x}, {y})");
            }
        }
    }

    [Test]
    public void Jbig2PageSmallerThanTheImageIsPaddedWhite()
    {
        byte[] page = [.. Jbig2Writer.Segment(0, 48, Jbig2Writer.PageInformation(3, 1, 1))];

        var bitmap = Decode("/Width 10 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode",
            page);

        Assert.That(Grays(bitmap), Is.EqualTo(new byte[]
        {
            0, 0, 0, 255, 255, 255, 255, 255, 255, 255,
            255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        }));
    }

    [Test]
    public void PremultipliedJpegTwoThousandColorsAreDividedByTheirOpacity()
    {
        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Jpeg2000");
        var data = File.ReadAllBytes(Path.Combine(folder, "rgba-lossless.jp2"));
        var file = File.ReadAllBytes(Path.Combine(folder, "rgba-lossless.rgba8"));

        var bitmap = Decode("/Width 157 /Height 93 /Filter /JPXDecode /SMaskInData 2", data);

        for (var i = 0; i < file.Length; i += 4)
        {
            var alpha = file[i + 3];
            for (var c = 0; c < 3; c++)
            {
                var expected = alpha == 0 ? 0 : System.Math.Min(255, file[i + c] * 255 / alpha);
                Assert.That(bitmap.Rgba[i + c], Is.EqualTo(expected));
            }

            Assert.That(bitmap.Rgba[i + 3], Is.EqualTo(alpha));
        }
    }

    [TestCase(1, true)]
    [TestCase(0, false)]
    public void JpegTwoThousandTakesItsOpacityOnlyWhenAsked(int inData, bool masked)
    {
        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Jpeg2000");
        var data = File.ReadAllBytes(Path.Combine(folder, "rgba-lossless.jp2"));
        var expected = File.ReadAllBytes(Path.Combine(folder, "rgba-lossless.rgba8"));

        var bitmap = Decode($"/Width 157 /Height 93 /Filter /JPXDecode /SMaskInData {inData} /Decode [1 0 1 0 1 0]",
            data);

        Assert.That((bitmap.Width, bitmap.Height), Is.EqualTo((157, 93)));
        for (var i = 0; i < expected.Length; i += 4)
        {
            Assert.That(bitmap.Rgba[i..(i + 3)], Is.EqualTo(expected[i..(i + 3)]), "the colors, /Decode left out");
            Assert.That(bitmap.Rgba[i + 3], Is.EqualTo(masked ? expected[i + 3] : 255));
        }
    }
}
