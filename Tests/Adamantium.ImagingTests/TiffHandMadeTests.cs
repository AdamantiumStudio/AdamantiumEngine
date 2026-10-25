using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Imaging.Tiff;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using static Adamantium.ImagingTests.HandMadeTiff;

namespace Adamantium.ImagingTests;

/// <summary>
/// TIFF the tools do not write, built field by field: odd depths and kinds, subsampled YCbCr, the TIFF 5.0 LZW, a
/// broken chain - and hostile files, which must fail fast and small with the exceptions the decoder promises.
/// </summary>
[TestFixture]
public class TiffHandMadeTests
{
    private const ushort Short = 3;
    private const ushort Long = 4;

    private static (ushort, ushort, ulong[]) Field(ushort tag, ushort type, params ulong[] values) => (tag, type, values);

    private static (ushort, ushort, ulong[])[] Strip(int width, int height, int bits, int samples, int photometric,
        int byteCount, int compression = 1) =>
    [
        Field(256, Long, (ulong)width), Field(257, Long, (ulong)height),
        Field(258, Short, Enumerable.Repeat((ulong)bits, samples).ToArray()), Field(259, Short, (ulong)compression),
        Field(262, Short, (ulong)photometric), Field(273, Long, Data), Field(277, Short, (ulong)samples),
        Field(278, Long, (ulong)height), Field(279, Long, (ulong)byteCount)
    ];

    private static long Allocated(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void SignedSamplesOf24Bits_KeepTheirSign()
    {
        var file = Build([0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x00],
            [.. Strip(2, 1, 24, 1, 1, 6), Field(339, Short, 2)]);

        var bitmap = TiffImage.Load(file);
        var pixels = bitmap.GetRawPixels(0);

        Assert.That(bitmap.PixelFormat, Is.EqualTo((SurfaceFormat)Format.R32_SINT));
        Assert.That((BinaryPrimitives.ReadInt32LittleEndian(pixels), BinaryPrimitives.ReadInt32LittleEndian(pixels.AsSpan(4))),
            Is.EqualTo((-1, 1)));
    }

    [Test]
    public void FloatsOf24Bits_AreWidened()
    {
        var file = Build([0x00, 0x00, 0x3F, 0x00, 0x40, 0xC0],
            [.. Strip(2, 1, 24, 1, 1, 6), Field(339, Short, 3)]);

        var pixels = TiffImage.Load(file).GetRawPixels(0);

        Assert.That((BinaryPrimitives.ReadSingleLittleEndian(pixels), BinaryPrimitives.ReadSingleLittleEndian(pixels.AsSpan(16))),
            Is.EqualTo((1f, -2.5f)));
    }

    [Test]
    public void SamplesOf8Bytes_KeepTheirValues()
    {
        byte[] data = [.. BitConverter.GetBytes(-5L), .. BitConverter.GetBytes(1L << 40)];
        var file = Build(data, [.. Strip(2, 1, 64, 1, 1, data.Length), Field(339, Short, 2)]);

        var bitmap = TiffImage.Load(file);

        Assert.That(bitmap.PixelFormat, Is.EqualTo((SurfaceFormat)Format.R64_SINT));
        Assert.That(bitmap.GetRawPixels(0), Is.EqualTo(data));
    }

    [Test]
    public void DoublesOf8Bytes_AreRead()
    {
        byte[] data = [.. BitConverter.GetBytes(2.5), .. BitConverter.GetBytes(-1e10)];
        var file = Build(data, [.. Strip(2, 1, 64, 1, 1, data.Length), Field(339, Short, 3)]);

        var pixels = TiffImage.Load(file).GetRawPixels(0);

        Assert.That((BinaryPrimitives.ReadSingleLittleEndian(pixels), BinaryPrimitives.ReadSingleLittleEndian(pixels.AsSpan(16))),
            Is.EqualTo((2.5f, -1e10f)));
    }

    // Palettes of 4 and 2 bits: indices packed two and four to a byte, each picking its entry of 16 bits a channel.
    [TestCase(4, new byte[] { 0x01, 0x2F })]
    [TestCase(2, new byte[] { 0b00_01_10_11 })]
    public void SmallPalettes_PickTheirEntries(int bits, byte[] data)
    {
        var entries = 1 << bits;
        var map = Enumerable.Range(0, 3 * entries).Select(i => (ulong)(i * 0x0F0F % 65536)).ToArray();
        var file = Build(data, [.. Strip(4, 1, bits, 1, 3, data.Length), Field(320, Short, map)]);

        var pixels = TiffImage.Load(file).GetRawPixels(0);

        int[] indices = bits == 4 ? [0, 1, 2, 15] : [0, 1, 2, 3];
        for (var i = 0; i < 4; i++)
        {
            Assert.That((pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2]),
                Is.EqualTo(((byte)(map[indices[i]] >> 8), (byte)(map[entries + indices[i]] >> 8),
                    (byte)(map[2 * entries + indices[i]] >> 8))), $"pixel {i}");
        }
    }

    [Test]
    public void SubsampledYCbCr_SpreadsEachUnitOverItsPixels()
    {
        byte[] units =
        [
            10, 20, 30, 40, 128, 128,
            50, 60, 70, 80, 128, 128,
            90, 91, 92, 93, 128, 128,
            100, 101, 102, 103, 128, 228
        ];
        var file = Build(units, [.. Strip(3, 3, 8, 3, 6, units.Length), Field(530, Short, 2, 2)]);

        var pixels = TiffImage.Load(file).GetRawPixels(0);

        byte[] gray = [10, 20, 50, 30, 40, 70, 90, 91, 100];
        for (var i = 0; i < 8; i++)
        {
            Assert.That((pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]),
                Is.EqualTo((gray[i], gray[i], gray[i], (byte)255)), $"pixel {i}");
        }

        Assert.That((pixels[32], pixels[34]), Is.EqualTo(((byte)240, (byte)100)), "red over luma 100");
    }

    [Test]
    public void LzwOfTiff5_IsRead()
    {
        int[] codes = [256, 'A', 'B', 'C', 'A', 257];
        var stream = new byte[(codes.Length * 9 + 7) / 8];
        var bit = 0;
        foreach (var code in codes)
        {
            for (var i = 0; i < 9; i++, bit++)
            {
                stream[bit / 8] |= (byte)(((code >> i) & 1) << (bit % 8));
            }
        }

        var file = Build(stream, Strip(4, 1, 8, 1, 1, stream.Length, 5));

        Assert.That(TiffImage.Load(file).GetRawPixels(0).Where((_, i) => i % 4 == 0),
            Is.EqualTo(new byte[] { 65, 66, 67, 65 }));
    }

    [Test]
    public void ABrokenLinkInTheChain_LeavesTheFirstPage()
    {
        var file = Build([1, 2, 3, 4], Strip(4, 1, 8, 1, 1, 4), next: 0x7FFF_FFF0);

        Assert.That(TiffImage.Load(file).GetRawPixels(0).Where((_, i) => i % 4 == 0),
            Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void DecodedFramesLetGo_DecodeAgainTheSame()
    {
        var bitmap = BitmapLoader.Load(Path.Combine(TiffDecodeTests.AssetsFolder(), "pages.tif"));
        var before = bitmap.GetRawPixels(2);

        bitmap.ReleaseDecodedFrames();

        Assert.That(bitmap.GetRawPixels(2), Is.EqualTo(before).And.Not.SameAs(before));
    }

    [Test]
    public void ReducedCopies_AreNotPages() =>
        Assert.That(BitmapLoader.Load(Path.Combine(TiffDecodeTests.AssetsFolder(), "pyramid.tif")),
            Has.Property("FramesCount").EqualTo(1).And.Property("Width").EqualTo(300));

    [Test]
    public void PagesOfAnotherSizeOrFormat_AreNotFrames() =>
        Assert.That(BitmapLoader.Load(Path.Combine(TiffDecodeTests.AssetsFolder(), "pages-mixed.tif")).FramesCount,
            Is.EqualTo(2));

    [Test]
    public void AStripClaimingGigabytes_IsReadAsFarAsTheFileGoes() =>
        Assert.That(Allocated(() => TiffImage.Load(Build([0x80, 0x00], Strip(4, 1, 8, 1, 1, 0x7FFFFFC0, 5)))),
            Is.LessThan(1 << 20));

    [TestCase(40000UL)]
    [TestCase(65536UL)]
    [TestCase(1UL << 24)]
    public void TilesFarLargerThanTheImage_AreDecodedOnlyWhereItIs(ulong tile)
    {
        (ushort, ushort, ulong[])[] fields =
        [
            Field(256, Long, 1), Field(257, Long, 1), Field(258, Short, 8), Field(259, Short, 1), Field(262, Short, 1),
            Field(322, Long, tile), Field(323, Long, tile), Field(324, Long, Data), Field(325, Long, 4)
        ];

        Assert.That(() => TiffImage.Load(Build([7, 0, 0, 0], fields)),
            Throws.InstanceOf<InvalidDataException>().Or.InstanceOf<NotSupportedException>());
        Assert.That(Allocated(() => { _ = Catch(() => TiffImage.Load(Build([7, 0, 0, 0], fields))); }),
            Is.LessThan(1 << 20));
    }

    // A run made of far more make-up codes than its row has room for ends the strip at the first one past the row.
    [Test]
    public void AFaxRunLongerThanItsRow_EndsTheStrip()
    {
        const int codes = 900_000;
        const string terminating = "00110101";
        var stream = new byte[(codes * 12 + terminating.Length + 7) / 8];
        for (long bit = 0; bit < codes * 12L + terminating.Length; bit++)
        {
            var one = bit < codes * 12L ? bit % 12 >= 7 : terminating[(int)(bit - codes * 12L)] == '1';
            if (one)
            {
                stream[bit / 8] |= (byte)(0x80 >> (int)(bit % 8));
            }
        }

        var file = Build(stream, Strip(16, 1, 1, 1, 0, stream.Length, 2));

        Assert.That(TiffImage.Load(file).GetRawPixels(0), Has.All.EqualTo(255));
    }

    [Test]
    public void APictureNoArrayHolds_IsRefusedBeforeDecoding() =>
        Assert.That(Allocated(() => Assert.That(() => TiffImage.Load(Build([0], Strip(42000, 42000, 8, 1, 1, 1, 5))),
            Throws.InstanceOf<NotSupportedException>())), Is.LessThan(1 << 20));

    [Test]
    public void AnUncompressedStripShorterThanItsRows_IsBroken() =>
        Assert.That(() => TiffImage.Load(Build([1, 2, 3], Strip(20000, 20000, 8, 1, 1, 3))),
            Throws.InstanceOf<InvalidDataException>());

    [Test]
    public void ManyFieldsOverOneLargeBlock_CostNothingUnread()
    {
        var block = new byte[256 * 1024];
        var fields = Strip(1, 1, 8, 1, 1, 1).ToList();
        for (ushort tag = 40000; tag < 40400; tag++)
        {
            fields.Add(Field(tag, Pointer, 1, (ulong)block.Length, Data));
        }

        var file = Build(block, [.. fields]);

        Assert.That(Allocated(() => TiffImage.Load(file)), Is.LessThan(8 << 20));
    }

    [Test]
    public void ABrokenJpegStrip_IsInvalidData()
    {
        var file = File.ReadAllBytes(Path.Combine(TiffDecodeTests.AssetsFolder(), "jpeg-ycbcr.tif"));
        var random = new Random(5);
        for (var run = 0; run < 300; run++)
        {
            var mutated = (byte[])file.Clone();
            for (var i = 0; i < 10; i++)
            {
                mutated[random.Next(16, mutated.Length)] = (byte)random.Next(256);
            }

            Assert.That(Catch(() => TiffImage.Load(mutated).GetRawPixels(0)), Is.Null
                .Or.InstanceOf<InvalidDataException>().Or.InstanceOf<NotSupportedException>(), $"run {run}");
        }
    }

    [TestCase("rgb8-lzw-predictor")]
    [TestCase("rgb8-tiled-lzw")]
    [TestCase("bilevel-g4")]
    [TestCase("bilevel-g3-2d")]
    [TestCase("rgba8-deflate")]
    [TestCase("rgb8-bigtiff")]
    [TestCase("gray-float32-predictor")]
    public void AMutatedFile_FailsOnlyAsPromised(string name)
    {
        var file = File.ReadAllBytes(Path.Combine(TiffDecodeTests.AssetsFolder(), name + ".tif"));
        var random = new Random(name.Length);
        for (var run = 0; run < 200; run++)
        {
            var mutated = (byte[])file.Clone();
            for (var i = 0; i < 4; i++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            Assert.That(Catch(() => TiffImage.Load(mutated).GetRawPixels(0)), Is.Null
                .Or.InstanceOf<InvalidDataException>().Or.InstanceOf<NotSupportedException>(), $"run {run}");
        }
    }

    private static Exception Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
