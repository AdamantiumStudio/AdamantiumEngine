using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adamantium.Imaging.Tiff;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// TiffReader gives the samples the whole-image decoder gives - tile by tile, window by window, band by band, from a
/// file or a stream, from many threads - and finds the reduced-resolution copies as levels.
/// </summary>
[TestFixture]
public class TiffReaderTests
{
    private static string File(string name) => Path.Combine(TiffDecodeTests.AssetsFolder(), name + ".tif");

    private static IEnumerable<string> Files() =>
        Directory.GetFiles(TiffDecodeTests.AssetsFolder(), "*.tif").Select(Path.GetFileNameWithoutExtension).Order();

    private static TiffSamples Whole(string name)
    {
        var tiff = TiffFile.Open(new TiffMemorySource(System.IO.File.ReadAllBytes(File(name))));
        var page = tiff.Directories.First(directory => directory.IsPage);
        return TiffDecoder.Decode(tiff.Source, TiffLayout.Read(page), tiff.LittleEndian);
    }

    [TestCaseSource(nameof(Files))]
    public void AWindowOverTheWholeLevel_IsTheWholeImage(string name)
    {
        using var reader = TiffReader.Open(File(name));
        var level = reader.Levels[0];
        var window = new byte[(long)level.Width * level.Height * level.Bands * level.BytesPerSample];

        reader.ReadWindow(0, 0, 0, level.Width, level.Height, window);

        Assert.That(window, Is.EqualTo(Whole(name).Data));
    }

    [TestCase("rgb8-tiled-lzw")]
    [TestCase("rgb8-planar")]
    [TestCase("rgb8-none-strips")]
    [TestCase("jpeg-tiled")]
    [TestCase("rgba-float32")]
    public void AnyWindowAndBand_IsItsPartOfTheWholeImage(string name)
    {
        var whole = Whole(name);
        using var reader = TiffReader.Open(new MemoryStream(System.IO.File.ReadAllBytes(File(name))));
        var level = reader.Levels[0];
        var pixelBytes = level.Bands * level.BytesPerSample;
        var random = new Random(name.Length);
        for (var run = 0; run < 40; run++)
        {
            var x = random.Next(level.Width);
            var y = random.Next(level.Height);
            var width = random.Next(1, level.Width - x + 1);
            var height = random.Next(1, level.Height - y + 1);
            var window = new byte[width * height * pixelBytes];

            reader.ReadWindow(0, x, y, width, height, window);

            for (var line = 0; line < height; line++)
            {
                Assert.That(window.AsSpan(line * width * pixelBytes, width * pixelBytes).ToArray(),
                    Is.EqualTo(whole.Data.AsSpan((int)whole.Offset(x, y + line, 0), width * pixelBytes).ToArray()),
                    $"window {width}×{height} at ({x}, {y}), line {line}");
            }
        }

        var band = new byte[level.TileWidth * level.TileHeight * level.BytesPerSample];
        reader.ReadTile(0, 0, 0, level.Bands - 1, band);
        for (var pixel = 0; pixel < Math.Min(level.TileWidth, level.Width); pixel++)
        {
            Assert.That(band.AsSpan(pixel * level.BytesPerSample, level.BytesPerSample).ToArray(),
                Is.EqualTo(whole.Data.AsSpan((int)whole.Offset(pixel, 0, level.Bands - 1), level.BytesPerSample)
                    .ToArray()), $"band pixel {pixel}");
        }
    }

    [Test]
    public void ReducedCopies_AreLevelsFromTheLargestDown()
    {
        using var reader = TiffReader.Open(File("pyramid"));

        Assert.That(reader.Levels.Select(level => (level.Width, level.Height)),
            Is.EqualTo(new[] { (300, 180), (150, 90), (75, 45) }));
        var smallest = reader.Levels[2];
        var window = new byte[smallest.Width * smallest.Height * smallest.Bands];
        reader.ReadWindow(2, 0, 0, smallest.Width, smallest.Height, window);
        Assert.That(window.Any(value => value != 0), Is.True, "the smallest copy holds a picture");
    }

    [Test]
    public void TilesReadAtOnce_AreTheTilesReadInTurn()
    {
        using var reader = TiffReader.Open(File("jpeg-tiled"));
        var level = reader.Levels[0];
        var tiles = level.TilesAcross * level.TilesDown;
        var inTurn = Enumerable.Range(0, tiles).Select(index =>
        {
            var tile = new byte[level.TileBytes];
            reader.ReadTile(0, index % level.TilesAcross, index / level.TilesAcross, tile);
            return tile;
        }).ToArray();

        for (var pass = 0; pass < 20; pass++)
        {
            var atOnce = new byte[tiles][];
            Parallel.For(0, tiles * 8, i =>
            {
                var index = i % tiles;
                var tile = new byte[level.TileBytes];
                reader.ReadTile(0, index % level.TilesAcross, index / level.TilesAcross, tile);
                atOnce[index] = tile;
            });

            Assert.That(atOnce, Is.EqualTo(inTurn), $"pass {pass}");
        }
    }

    // 100000 × 100000 RGB, every tile the same compressed block: 30 GB of pixels in a file of a megabyte or so.
    [Test]
    public void ARasterNoMemoryHolds_IsReadAWindowAtATime()
    {
        const int side = 100_000;
        const int tile = 256;
        var pixels = new byte[tile * tile * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 3 * 100 + i / 3 % tile);
        }

        var block = TiffDeflate.Encode(pixels);
        var tiles = (ulong)Math.Pow((side + tile - 1) / tile, 2);
        (ushort, ushort, ulong[])[] fields =
        [
            (256, 4, [side]), (257, 4, [side]), (258, 3, [8, 8, 8]), (259, 3, [8]), (262, 3, [2]), (277, 3, [3]),
            (322, 4, [tile]), (323, 4, [tile]), (324, 4, Enumerable.Repeat(HandMadeTiff.Data, (int)tiles).ToArray()),
            (325, 4, Enumerable.Repeat((ulong)block.Length, (int)tiles).ToArray())
        ];
        var path = Path.Combine(Path.GetTempPath(), $"tiff-reader-{Guid.NewGuid():N}.tif");
        System.IO.File.WriteAllBytes(path, HandMadeTiff.Build(block, fields));
        try
        {
            Assert.That(() => TiffImage.Load(System.IO.File.ReadAllBytes(path)), Throws.InstanceOf<NotSupportedException>());

            var before = GC.GetAllocatedBytesForCurrentThread();
            using var reader = TiffReader.Open(path);
            var window = new byte[300 * 200 * 3];
            reader.ReadWindow(0, 50_000 - 150, 50_000 - 100, 300, 200, window);

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(64 << 20));
            var x = (50_000 - 150) % tile;
            var y = (50_000 - 100) % tile;
            Assert.That(window.AsSpan(0, 3).ToArray(), Is.EqualTo(pixels.AsSpan((y * tile + x) * 3, 3).ToArray()));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static (ushort, ushort, ulong[])[] TiledGray(int side, int tile, ulong[] offsets, ulong[] counts,
        ushort compression = 1) =>
    [
        (256, 4, [(ulong)side]), (257, 4, [(ulong)side]), (258, 3, [8]), (259, 3, [compression]), (262, 3, [1]),
        (322, 4, [(ulong)tile]), (323, 4, [(ulong)tile]), (324, 4, offsets), (325, 4, counts)
    ];

    // 20 × 20 in tiles of 16: the file pads the edge tiles with 0xAA, which must not come out as pixels.
    [Test]
    public void AnEdgeTile_IsZeroPastTheLevel()
    {
        var tile = Enumerable.Repeat((byte)0xAA, 256).ToArray();
        for (var i = 0; i < 4; i++)
        {
            tile[i] = (byte)(17 + i);
        }

        var file = HandMadeTiff.Build(tile, TiledGray(20, 16, [8, 8, 8, 8], [256, 256, 256, 256]));
        using var reader = TiffReader.Open(new MemoryStream(file));
        var read = new byte[256];

        reader.ReadTile(0, 1, 1, read);

        Assert.That(reader.Levels[0].TileArea(1, 1), Is.EqualTo((4, 4)));
        Assert.That(read.Take(4), Is.EqualTo(new byte[] { 17, 18, 19, 20 }));
        Assert.That(read.Skip(4).Take(12), Has.All.EqualTo(0), "past the right edge");
        Assert.That(read.Skip(16 * 4), Has.All.EqualTo(0), "below the bottom edge");
    }

    [Test]
    public void ATileTheFileLeavesOut_IsKnownAndZero()
    {
        var file = HandMadeTiff.Build(new byte[256], TiledGray(32, 16, [8, 0, 8, 8], [256, 0, 256, 256]));
        using var reader = TiffReader.Open(new MemoryStream(file));

        Assert.That((reader.HasTile(0, 0, 0), reader.HasTile(0, 1, 0)), Is.EqualTo((true, false)));
    }

    // GDAL's sparse files: a tile left out is no data, read as the no-data value when the samples can hold it.
    [TestCase("7", 7)]
    [TestCase("-1", 0)]
    [TestCase("2.5", 0)]
    public void ATileTheFileLeavesOut_ReadsAsNoData(string noData, int expected)
    {
        var text = Encoding.ASCII.GetBytes(noData + "\0").Select(b => (ulong)b).ToArray();
        var file = HandMadeTiff.Build(Enumerable.Repeat((byte)5, 256).ToArray(),
            [.. TiledGray(32, 16, [8, 0, 8, 8], [256, 0, 256, 256]), (42113, 2, text)]);
        using var reader = TiffReader.Open(new MemoryStream(file));
        var window = new byte[32 * 32];
        var tile = new byte[256];

        reader.ReadWindow(0, 0, 0, 32, 32, window);
        reader.ReadTile(0, 1, 0, tile);

        Assert.That((window[0], window[16], window[16 * 32 + 16]), Is.EqualTo((5, expected, 5)));
        Assert.That(tile, Has.All.EqualTo(expected));
    }

    // A tile that claims a megabyte for 256 pixels is read, and kept, only as far as any codec needs: 16 times its
    // pixels and 64 KB.
    [Test]
    public void ACompressedTileOfPadding_IsNotReadPastWhatACodecNeeds()
    {
        var file = HandMadeTiff.Build(new byte[1 << 20], TiledGray(16, 16, [8], [1 << 20], compression: 5));
        var tiff = TiffFile.Open(new TiffMemorySource(file));

        Assert.That(TiffChunkDecoder.InputBytes(TiffLayout.Read(tiff.Directories[0])), Is.EqualTo(16 * 256 + 65536));
    }

    // Integers of 64 bits past 2^53 fill a tile left out as written, not as near as a double comes.
    [TestCase(1, "9223372036854775807", 9223372036854775807UL)]
    [TestCase(1, "18446744073709551615", ulong.MaxValue)]
    [TestCase(2, "-9223372036854775808", 0x8000000000000000UL)]
    [TestCase(2, "9007199254740993", 9007199254740993UL)]
    public void A64BitNoData_FillsATileLeftOutExactly(int format, string noData, ulong expected)
    {
        var text = Encoding.ASCII.GetBytes(noData + "\0").Select(b => (ulong)b).ToArray();
        (ushort, ushort, ulong[])[] fields =
        [
            (256, 4, [32]), (257, 4, [16]), (258, 3, [64]), (259, 3, [1]), (262, 3, [1]), (322, 4, [16]),
            (323, 4, [16]), (324, 4, [8, 0]), (325, 4, [2048, 0]), (339, 3, [(ulong)format]), (42113, 2, text)
        ];
        using var reader = TiffReader.Open(new MemoryStream(HandMadeTiff.Build(new byte[2048], fields)));
        var tile = new byte[2048];

        reader.ReadTile(0, 1, 0, tile);

        Assert.That(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(tile).ToArray(),
            Has.All.EqualTo(expected));
    }

    [Test]
    public void SubsampledYCbCr_ComesOutAsSamplesPerPixel()
    {
        byte[] units = [10, 20, 30, 40, 100, 150, 50, 60, 70, 80, 110, 160];
        (ushort, ushort, ulong[])[] fields =
        [
            (256, 4, [4]), (257, 4, [2]), (258, 3, [8, 8, 8]), (259, 3, [1]), (262, 3, [6]), (273, 4, [8]),
            (277, 3, [3]), (278, 4, [2]), (279, 4, [(ulong)units.Length]), (530, 3, [2, 2])
        ];
        using var reader = TiffReader.Open(new MemoryStream(HandMadeTiff.Build(units, fields)));
        var window = new byte[4 * 2 * 3];

        reader.ReadWindow(0, 0, 0, 4, 2, window);

        Assert.That(window, Is.EqualTo(new byte[]
        {
            10, 100, 150, 20, 100, 150, 50, 110, 160, 60, 110, 160,
            30, 100, 150, 40, 100, 150, 70, 110, 160, 80, 110, 160
        }));
    }

    [Test]
    public void ACompressionNotRead_IsRefusedWhenOpened() =>
        Assert.That(() => TiffReader.Open(new MemoryStream(HandMadeTiff.Build(new byte[16],
            TiledGray(16, 16, [8], [16], 50000)))), Throws.InstanceOf<NotSupportedException>());

    [Test]
    public void AfterDispose_NothingIsRead()
    {
        var stream = new MemoryStream(System.IO.File.ReadAllBytes(File("rgb8-tiled-lzw")));
        var reader = TiffReader.Open(stream, leaveOpen: true);
        reader.Dispose();

        Assert.That(() => reader.ReadTile(0, 0, 0, new byte[16 * 16 * 3]), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(stream.CanRead, Is.True, "the stream was to be left open");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AFileThatCannotBeOpened_LeavesTheStreamAsAsked(bool leaveOpen)
    {
        var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9]);

        Assert.That(() => TiffReader.Open(stream, leaveOpen), Throws.InstanceOf<InvalidDataException>());
        Assert.That(stream.CanRead, Is.EqualTo(leaveOpen));
    }

    // A page with thousands of SubIFD offsets at one directory of thousands of tiles: a 50 KB file.
    [Test]
    public void ReducedCopiesByTheThousand_CostLittleAndCountOnce()
    {
        var tiles = Enumerable.Repeat(HandMadeTiff.Data, 4096).ToArray();
        var counts = Enumerable.Repeat(1UL, 4096).ToArray();
        const ulong directory = 10;
        var file = HandMadeTiff.Build([0, 0],
            [.. TiledGray(1024, 16, tiles, counts), (330, 4, Enumerable.Repeat(directory, 4000).ToArray())]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var reader = TiffReader.Open(new MemoryStream(file));

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(16 << 20));
        Assert.That(reader.Levels, Has.Count.EqualTo(1));
    }

    // 46000 × 46000 in one Deflate strip the file leaves out: 2 GB decoded, never asked for.
    [Test]
    public void AStripTheFileLeavesOut_CostsNothingToOpenOrRead()
    {
        (ushort, ushort, ulong[])[] fields =
        [
            (256, 4, [46000]), (257, 4, [46000]), (258, 3, [8]), (259, 3, [8]), (262, 3, [1]), (273, 4, [0]),
            (278, 4, [46000]), (279, 4, [0])
        ];
        var file = HandMadeTiff.Build([0, 0], fields);
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var reader = TiffReader.Open(new MemoryStream(file));
        var window = new byte[64 * 64];

        reader.ReadWindow(0, 1000, 1000, 64, 64, window);

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(16 << 20));
        Assert.That(window, Has.All.EqualTo(0));
    }

    [Test]
    public void AJpegScanWithoutItsEnd_EndsInsteadOfRunningForever()
    {
        var file = System.IO.File.ReadAllBytes(TestAssets.Path("TiffHostile/jpeg-scan-without-end.tif"));
        var read = Task.Run(() =>
        {
            try
            {
                using var reader = TiffReader.Open(new MemoryStream(file));
                var level = reader.Levels[0];
                reader.ReadWindow(0, 0, 0, level.Width, level.Height,
                    new byte[(long)level.Width * level.Height * level.Bands]);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
            }
        });

        Assert.That(read.Wait(TimeSpan.FromSeconds(10)), Is.True);
    }

    [Test]
    public void LevelsAndColorMaps_CannotBeChanged()
    {
        using var reader = TiffReader.Open(File("palette8"));

        Assert.That(reader.Levels, Is.Not.InstanceOf<TiffLevel[]>());
        Assert.That(reader.Levels[0].ColorMap, Is.Not.InstanceOf<ushort[]>());
    }

    [Test]
    public void ATileOrWindowOutsideTheLevel_IsRefused()
    {
        using var reader = TiffReader.Open(File("rgb8-tiled-lzw"));
        var level = reader.Levels[0];

        Assert.Multiple(() =>
        {
            Assert.That(() => reader.ReadTile(0, level.TilesAcross, 0, new byte[level.TileBytes]),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => reader.ReadTile(0, 0, 0, new byte[level.TileBytes - 1]), Throws.ArgumentException);
            Assert.That(() => reader.ReadWindow(0, 1, 0, level.Width, 1, new byte[level.Width * 3]),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => reader.ReadTile(1, 0, 0, new byte[level.TileBytes]),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }
}
