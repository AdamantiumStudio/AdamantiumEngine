using System;
using System.Text;
using Adamantium.Pdf;
using Adamantium.Pdf.CrossReference;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfFilterTests
{
    private static byte[] Decode(string dictionary, byte[] data, PdfDecodeLimits limits = null)
    {
        var parsed = (PdfDictionary)new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(dictionary))).ReadObject();
        return PdfStreamDecoder.Decode(parsed, data, PdfDirectResolver.Instance, limits).Data.ToArray();
    }

    [Test]
    public void LzwDecodesTheSpecificationsExample()
    {
        byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];
        var decoded = Decode("<< /Filter /LZWDecode >>", encoded);
        Assert.That(decoded, Is.EqualTo(new byte[] { 45, 45, 45, 45, 45, 65, 45, 45, 45, 66 }));
    }

    [Test]
    public void LzwGrowsItsOutputPastTheFirstGuess()
    {
        var input = new byte[100_000];
        new Random(7).NextBytes(input.AsSpan(0, 1000));
        var encoded = LzwTestEncoder.Encode(input);
        Assert.That(encoded.Length * 4, Is.LessThan(input.Length));
        Assert.That(Decode("<< /Filter /LZWDecode >>", encoded), Is.EqualTo(input));
    }

    [Test]
    public void LzwReadsCodesWithoutTheEarlyChange()
    {
        var input = new byte[20_000];
        new Random(3).NextBytes(input);
        var encoded = LzwTestEncoder.Encode(input, false);
        Assert.That(Decode("<< /Filter /LZWDecode /DecodeParms << /EarlyChange 0 >> >>", encoded), Is.EqualTo(input));
        Assert.That(Decode("<< /Filter /LZWDecode >>", encoded), Is.Not.EqualTo(input));
    }

    [Test]
    public void FlateWithPngPredictorUndoesEveryRowFilter()
    {
        byte[] rows =
        [
            0, 10, 20, 30,
            1, 5, 5, 5,
            2, 1, 1, 1,
            3, 4, 4, 4,
            4, 1, 2, 3,
        ];
        var decoded = Decode("<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 3 >> >>",
            PdfFileBuilder.Flate(rows));
        Assert.That(decoded, Is.EqualTo(new byte[] { 10, 20, 30, 5, 10, 15, 6, 11, 16, 7, 13, 18, 8, 15, 21 }));
    }

    [Test]
    public void TiffPredictorAddsTheComponentToTheLeft()
    {
        var decoded = Decode("<< /Filter /FlateDecode /DecodeParms << /Predictor 2 /Colors 3 /Columns 2 >> >>",
            PdfFileBuilder.Flate([10, 20, 30, 1, 2, 3]));
        Assert.That(decoded, Is.EqualTo(new byte[] { 10, 20, 30, 11, 22, 33 }));
    }

    [Test]
    public void FlateWithoutAZlibHeaderReadsAsRawDeflate()
    {
        var zlib = PdfFileBuilder.Flate(Encoding.ASCII.GetBytes("raw deflate body"));
        var raw = zlib.AsSpan(2, zlib.Length - 6).ToArray();
        Assert.That(Encoding.ASCII.GetString(Decode("<< /Filter /FlateDecode >>", raw)), Is.EqualTo("raw deflate body"));
    }

    [Test]
    public void FlateCutShortGivesWhatItHolds()
    {
        var text = new string('x', 5000) + new string('y', 5000);
        var zlib = PdfFileBuilder.Flate(Encoding.ASCII.GetBytes(text));
        var decoded = Decode("<< /Filter /FlateDecode >>", zlib.AsSpan(0, zlib.Length - 4).ToArray());
        Assert.That(Encoding.ASCII.GetString(decoded), Is.EqualTo(text));
    }

    [Test]
    public void AStreamDecodingPastTheLimitIsRefused()
    {
        var bomb = PdfFileBuilder.Flate(new byte[10_000_000]);
        var limits = new PdfDecodeLimits { MaxDecodedLength = 1_000_000 };
        Assert.Throws<PdfDecodeLimitException>(() => Decode("<< /Filter /FlateDecode >>", bomb, limits));
    }

    [Test]
    public void AStreamExactlyAtTheLimitIsKept()
    {
        var limits = new PdfDecodeLimits { MaxDecodedLength = 100_000 };
        Assert.That(Decode("<< /Filter /FlateDecode >>", PdfFileBuilder.Flate(new byte[100_000]), limits).Length,
            Is.EqualTo(100_000));
        Assert.That(Decode("<< /Filter /LZWDecode >>", LzwTestEncoder.Encode(new byte[100_000]), limits).Length,
            Is.EqualTo(100_000));
        Assert.Throws<PdfDecodeLimitException>(() =>
            Decode("<< /Filter /LZWDecode >>", LzwTestEncoder.Encode(new byte[100_001]), limits));
        Assert.Throws<PdfDecodeLimitException>(() =>
            Decode("<< /Filter /A85 >>", Encoding.ASCII.GetBytes(new string('z', 30_000) + "~>"), limits));
    }

    [Test]
    public void APredictorWiderThanItsDataAllocatesNothingHuge()
    {
        var decoded = Decode("<< /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 900000000 >> >>",
            PdfFileBuilder.Flate([2, 1, 2, 3]));
        Assert.That(decoded, Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void AsciiFiltersDecode()
    {
        Assert.That(Encoding.ASCII.GetString(Decode("<< /Filter /ASCII85Decode >>", Encoding.ASCII.GetBytes("9jqo^~>"))),
            Is.EqualTo("Man "));
        Assert.That(Decode("<< /Filter /A85 >>", Encoding.ASCII.GetBytes("z~>")), Is.EqualTo(new byte[4]));
        Assert.That(Encoding.ASCII.GetString(Decode("<< /Filter /ASCII85Decode >>", Encoding.ASCII.GetBytes("9jqo~>"))),
            Is.EqualTo("Man"));
        Assert.That(Decode("<< /Filter /ASCIIHexDecode >>", Encoding.ASCII.GetBytes("61 62\n6>")),
            Is.EqualTo(new byte[] { 0x61, 0x62, 0x60 }));
    }

    [Test]
    public void RunLengthCopiesRepeatsAndStopsAtTheEnd()
    {
        byte[] encoded = [2, 1, 2, 3, 254, 9, 128, 7, 7];
        Assert.That(Decode("<< /Filter /RunLengthDecode >>", encoded), Is.EqualTo(new byte[] { 1, 2, 3, 9, 9, 9 }));
    }

    [Test]
    public void ChainsApplyInOrderAndStopAtAnImageFilter()
    {
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(PdfFileBuilder.Flate([1, 2, 3])) + ">");
        Assert.That(Decode("<< /Filter [/AHx /Fl] >>", hex), Is.EqualTo(new byte[] { 1, 2, 3 }));
        var parsed = (PdfDictionary)new PdfObjectParser(new PdfLexer(
            Encoding.ASCII.GetBytes("<< /Filter [/ASCIIHexDecode /DCTDecode] >>"))).ReadObject();
        var decoded = PdfStreamDecoder.Decode(parsed, Encoding.ASCII.GetBytes("FFD8>"), PdfDirectResolver.Instance);
        Assert.That(decoded.ImageFilter, Is.EqualTo("DCTDecode"));
        Assert.That(decoded.Data.ToArray(), Is.EqualTo(new byte[] { 0xFF, 0xD8 }));
    }
}
