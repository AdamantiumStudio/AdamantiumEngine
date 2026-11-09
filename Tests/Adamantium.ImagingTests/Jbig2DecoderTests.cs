using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Jbig2;
using Adamantium.Imaging.Mq;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

[TestFixture]
public class Jbig2DecoderTests
{
    private const long MaxPixels = 1 << 24;

    [Test]
    public void ArithmeticCodeRoundTripsInManyContexts()
    {
        var random = new Random(7);
        var decisions = new (int Context, int Bit)[20000];
        for (var i = 0; i < decisions.Length; i++)
        {
            var context = random.Next(8);
            decisions[i] = (context, random.Next(100) < context * 12 ? 1 : 0);
        }

        var encoder = new MqEncoder();
        var encoding = new byte[8];
        foreach (var (context, bit) in decisions)
        {
            encoder.Encode(encoding, context, bit);
        }

        var coded = encoder.Finish();
        var decoder = new MqDecoder(coded, 0, coded.Length);
        var decoding = new byte[8];
        foreach (var (context, bit) in decisions)
        {
            Assert.That(decoder.Decode(decoding, context), Is.EqualTo(bit));
        }
    }

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public void GenericRegionDecodesEachTemplate(int template, bool typical)
    {
        var bitmap = Picture(77, 41);
        var page = Decode(Jbig2Writer.GenericPage(bitmap, template, DefaultAt(template), typical), out var w,
            out var h);

        Assert.That((w, h), Is.EqualTo((77, 41)));
        Assert.That(page, Is.EqualTo(Pack(bitmap)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void GenericRegionReadsItsAdaptivePixels(int template)
    {
        var bitmap = Picture(50, 30);
        int[] at = template == 0 ? [-5, -1, 4, -2, -1, -3, 0, -4] : [-6, -2];

        var page = Decode(Jbig2Writer.GenericPage(bitmap, template, at, true), out _, out _);

        Assert.That(page, Is.EqualTo(Pack(bitmap)));
    }

    [Test]
    public void TextRegionPlacesSymbolsOfItsDictionary()
    {
        List<byte[,]> symbols = [Picture(9, 12), Picture(14, 7), Picture(5, 5)];
        List<(int Id, int X, int Y)> placements = [(0, 3, 2), (1, 20, 5), (2, 1, 20), (0, 30, 18), (1, 26, 0)];
        byte[] data =
        [
            .. Jbig2Writer.Segment(0, 0, Jbig2Writer.SymbolDictionary(symbols)),
            .. Jbig2Writer.Segment(1, 48, Jbig2Writer.PageInformation(48, 32)),
            .. Jbig2Writer.Segment(2, 6, Jbig2Writer.TextRegion(48, 32, symbols.Count, placements, symbols), 0),
            .. Jbig2Writer.Segment(3, 49, []),
        ];
        Assert.That(Decode(data, out _, out _), Is.EqualTo(Pack(Placed(48, 32, symbols, placements))));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void TextRegionRefinesItsSymbols(int template)
    {
        List<byte[,]> symbols = [Picture(9, 12), Picture(14, 7)];
        var wider = Picture(11, 12);
        var shorter = Picture(14, 5);
        List<(int Id, int X, int Y, byte[,] Refined, int Rdx, int Rdy)> placements =
            [(0, 3, 2, wider, 0, 0), (1, 20, 5, null, 0, 0), (1, 18, 20, shorter, -1, 1), (0, 33, 15, null, 0, 0)];
        byte[] data =
        [
            .. Jbig2Writer.Segment(0, 0, Jbig2Writer.SymbolDictionary(symbols)),
            .. Jbig2Writer.Segment(1, 48, Jbig2Writer.PageInformation(48, 32)),
            .. Jbig2Writer.Segment(2, 6, Jbig2Writer.RefinedTextRegion(48, 32, 2, placements, symbols, template), 0),
        ];
        List<byte[,]> drawn = [wider, symbols[1], shorter, symbols[0]];
        List<(int Id, int X, int Y)> at = [(0, 3, 2), (1, 20, 5), (2, 18, 20), (3, 33, 15)];

        Assert.That(Decode(data, out _, out _), Is.EqualTo(Pack(Placed(48, 32, drawn, at))));
    }

    private static byte[,] Placed(int width, int height, List<byte[,]> symbols,
        List<(int Id, int X, int Y)> placements)
    {
        var page = new byte[height, width];
        foreach (var (id, x, y) in placements)
        {
            var symbol = symbols[id];
            for (var sy = 0; sy < symbol.GetLength(0); sy++)
            {
                for (var sx = 0; sx < symbol.GetLength(1); sx++)
                {
                    if (x + sx < width && y + sy < height)
                    {
                        page[y + sy, x + sx] |= symbol[sy, sx];
                    }
                }
            }
        }

        return page;
    }

    [Test]
    public void GlobalsComeBeforeThePage()
    {
        List<byte[,]> symbols = [Picture(9, 12)];
        var globals = Jbig2Writer.Segment(0, 0, Jbig2Writer.SymbolDictionary(symbols));
        byte[] data =
        [
            .. Jbig2Writer.Segment(1, 48, Jbig2Writer.PageInformation(16, 16)),
            .. Jbig2Writer.Segment(2, 6, Jbig2Writer.TextRegion(16, 16, 1, [(0, 2, 2)], symbols), 0),
        ];

        var page = Jbig2Decoder.Decode(data, globals, MaxPixels, out var width, out var height);

        Assert.That((width, height), Is.EqualTo((16, 16)));
        Assert.That(page, Is.EqualTo(Pack(Placed(16, 16, symbols, [(0, 2, 2)]))));
    }

    [Test]
    public void PageOfDefaultPixelOneIsBlack()
    {
        byte[] data = [.. Jbig2Writer.Segment(0, 48, Jbig2Writer.PageInformation(10, 2, 1))];

        Assert.That(Decode(data, out _, out _), Is.EqualTo(new byte[] { 0xFF, 0xC0, 0xFF, 0xC0 }));
    }

    [Test]
    public void PageBiggerThanTheLimitIsRefused()
    {
        byte[] data = [.. Jbig2Writer.Segment(0, 48, Jbig2Writer.PageInformation(100000, 100000))];

        Assert.Throws<InvalidDataException>(() => Jbig2Decoder.Decode(data, [], MaxPixels, out _, out _));
    }

    [Test]
    public void BytesAfterTheLastSegmentKeepThePage()
    {
        byte[] data = [.. Jbig2Writer.Segment(0, 48, Jbig2Writer.PageInformation(10, 2, 1)), 0x00, 0x01];

        Assert.That(Decode(data, out _, out _), Is.EqualTo(new byte[] { 0xFF, 0xC0, 0xFF, 0xC0 }));
    }

    [Test]
    public void RefinementOfAHugeRegionIsRefusedBeforeItIsMade()
    {
        var region = new MemoryStream();
        region.Write(Jbig2Writer.RegionInfo(46000, 46000, 0, 0, 0));
        region.WriteByte(1);
        byte[] data =
        [
            .. Jbig2Writer.Segment(0, 48, Jbig2Writer.PageInformation(1, 1)),
            .. Jbig2Writer.Segment(1, 42, region.ToArray()),
        ];
        var before = GC.GetTotalAllocatedBytes();

        Jbig2Decoder.Decode(data, [], 1 << 20, out _, out _);

        Assert.That(GC.GetTotalAllocatedBytes() - before, Is.LessThan(64L << 20));
    }

    [Test]
    public void ABigSymbolPlacedOverAndOverStopsAtTheBudget()
    {
        var symbol = new byte[512, 512];
        List<byte[,]> symbols = [symbol];
        var placements = new List<(int Id, int X, int Y)>();
        for (var i = 0; i < 20000; i++)
        {
            placements.Add((0, 0, 0));
        }

        byte[] data =
        [
            .. Jbig2Writer.Segment(0, 0, Jbig2Writer.SymbolDictionary(symbols)),
            .. Jbig2Writer.Segment(1, 48, Jbig2Writer.PageInformation(512, 512)),
            .. Jbig2Writer.Segment(2, 6, Jbig2Writer.TextRegion(512, 512, 1, placements, symbols), 0),
        ];
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Jbig2Decoder.Decode(data, [], 1 << 20, out _, out _);

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
    }

    [Test]
    public void MutatedStreamsFailOnlyAsPromised()
    {
        List<byte[,]> symbols = [Picture(9, 12), Picture(14, 7)];
        byte[] data =
        [
            .. Jbig2Writer.Segment(0, 0, Jbig2Writer.SymbolDictionary(symbols)),
            .. Jbig2Writer.Segment(1, 48, Jbig2Writer.PageInformation(48, 32)),
            .. Jbig2Writer.Segment(2, 6, Jbig2Writer.TextRegion(48, 32, 2, [(0, 3, 2), (1, 20, 5)], symbols), 0),
            .. Jbig2Writer.Segment(3, 38, Jbig2Writer.GenericRegion(Picture(20, 10), 4, 20, 0,
                DefaultAt(0), true)),
        ];
        var random = new Random(3);
        for (var run = 0; run < 2000; run++)
        {
            var mutated = (byte[])data.Clone();
            for (var i = 0; i < 3; i++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            if (run % 10 == 0)
            {
                Array.Resize(ref mutated, random.Next(mutated.Length));
            }

            try
            {
                Jbig2Decoder.Decode(mutated, [], MaxPixels, out _, out _);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
            }
        }
    }

    private static int[] DefaultAt(int template) =>
        template == 0 ? [3, -1, -3, -1, 2, -2, -2, -2] : template == 1 ? [3, -1] : [2, -1];

    private static byte[] Decode(byte[] data, out int width, out int height) =>
        Jbig2Decoder.Decode(data, [], MaxPixels, out width, out height);

    private static byte[,] Picture(int width, int height)
    {
        var bitmap = new byte[height, width];
        for (var y = 0; y < height; y++)
        {
            var source = y % 7 == 3 && y > 0 ? y - 1 : y;
            for (var x = 0; x < width; x++)
            {
                var dx = x - width / 2;
                var dy = source - height / 2;
                var inside = dx * dx + 2 * dy * dy < width * height / 5;
                var stripe = (x + source) % 9 < 2;
                bitmap[y, x] = (byte)(inside ^ stripe ? 1 : 0);
            }
        }

        return bitmap;
    }

    private static byte[] Pack(byte[,] bitmap)
    {
        var height = bitmap.GetLength(0);
        var width = bitmap.GetLength(1);
        var rowBytes = (width + 7) / 8;
        var rows = new byte[rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                rows[y * rowBytes + x / 8] |= (byte)(bitmap[y, x] << (7 - x % 8));
            }
        }

        return rows;
    }
}
