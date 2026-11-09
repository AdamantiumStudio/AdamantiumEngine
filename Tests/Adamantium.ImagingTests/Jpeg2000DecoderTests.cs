using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg2000;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>JPEG 2000 files OpenJPEG wrote, read against what OpenJPEG reads from them (see make-assets.py): the
/// reversible ones to the bit, the irreversible ones within the rounding of the 9-7 wavelet.</summary>
[TestFixture]
public class Jpeg2000DecoderTests
{
    private const long MaxPixels = 1 << 24;

    private static string Folder() => TestAssets.Path("Jpeg2000");

    private static IEnumerable<string> EightBit() =>
        Directory.GetFiles(Folder(), "*.rgba8").Select(Path.GetFileNameWithoutExtension)
            .Where(name => !name.Contains('@')).Order();

    private static IEnumerable<string> Partial() =>
        Directory.GetFiles(Folder(), "*@*.rgba8").Select(Path.GetFileNameWithoutExtension).Order();

    [TestCaseSource(nameof(Partial))]
    public void PartsReadAsOpenJpegReadsThem(string name)
    {
        var at = name.IndexOf('@');
        var discard = int.Parse(name[(at + 2)..name.IndexOf('l', at)]);
        var layers = int.Parse(name[(name.IndexOf('l', at) + 1)..]);
        var data = File.ReadAllBytes(Path.Combine(Folder(), name[..at] + ".jp2"));

        var image = Jpeg2000Decoder.Decode(data, MaxPixels,
            new Jpeg2000Options { DiscardLevels = discard, Layers = layers });

        var expected = File.ReadAllBytes(Path.Combine(Folder(), name + ".rgba8"));
        Assert.That(image.Width * image.Height * 4, Is.EqualTo(expected.Length), "the size");
        var tolerance = name.StartsWith("rgb-layers") ? 1 : 0;
        Assert.That(MaxDifference(Rgba(image), expected), Is.LessThanOrEqualTo(tolerance));
    }

    [Test]
    public void TheLoaderGivesResolutionsAsMipLevelsAndLayersAsQualityLevels()
    {
        IRawBitmap bitmap = BitmapLoader.Load(Path.Combine(Folder(), "rgb-layers.jp2"));

        Assert.That((bitmap.Width, bitmap.Height), Is.EqualTo((157u, 93u)));
        Assert.That((bitmap.MipLevelsCount, bitmap.QualityLevelsCount), Is.EqualTo((6u, 3u)));
        var level = bitmap.GetLevelData(1, 2);
        Assert.That((level.Description.Width, level.Description.Height), Is.EqualTo((79u, 47u)));
        var expected = File.ReadAllBytes(Path.Combine(Folder(), "rgb-layers@r1l1.rgba8"));
        Assert.That(MaxDifference(level.RawPixels, expected), Is.LessThanOrEqualTo(1));
        Assert.That(bitmap.GetLevelData(1, 2), Is.SameAs(level), "kept");
        Assert.That(bitmap.GetMipLevelData(2).Description.Width, Is.EqualTo(40u));
        Assert.That(bitmap.GetLevelData(6, 0), Is.Null);
        Assert.That(bitmap.GetLevelData(0, 3), Is.Null);
    }

    [Test]
    public void ABareCodestreamIsKnownByItsSignature()
    {
        IRawBitmap bitmap = BitmapLoader.Load(Path.Combine(Folder(), "rgb-codestream.j2k"));

        Assert.That(bitmap.PixelFormat, Is.EqualTo(SurfaceFormat.R8G8B8A8.UNorm));
        Assert.That(MaxDifference(bitmap.GetRawPixels(0),
            File.ReadAllBytes(Path.Combine(Folder(), "rgb-codestream.rgba8"))), Is.Zero);
    }

    [Test]
    public void APictureOfOneQualityHasOneQualityLevel()
    {
        IRawBitmap bitmap = BitmapLoader.Load(TestAssets.Path("Test.png"));

        Assert.That(bitmap.QualityLevelsCount, Is.EqualTo(1u));
        Assert.That(bitmap.GetLevelData(0, 1), Is.Null);
        Assert.That(bitmap.GetLevelData(0, 0), Is.Not.Null);
    }

    [Test]
    public void ReducedSizeIsTheCeilingOfTheGridOverTheScale()
    {
        var data = File.ReadAllBytes(Path.Combine(Folder(), "rgb-tiles-offset.jp2"));

        var image = Jpeg2000Decoder.Decode(data, MaxPixels, new Jpeg2000Options { DiscardLevels = 2 });

        Assert.That((image.Width, image.Height), Is.EqualTo((41 - 2, 25 - 2)), "ceil(164 / 4) - ceil(7 / 4)");
    }

    [Test]
    public void FewerLevelsAndLayersReadFewerBytes()
    {
        var data = File.ReadAllBytes(Path.Combine(Folder(), "rgb-layers.jp2"));

        var full = Jpeg2000Decoder.Decode(data, MaxPixels);
        var oneLayer = Jpeg2000Decoder.Decode(data, MaxPixels, new Jpeg2000Options { Layers = 1 });
        var small = Jpeg2000Decoder.Decode(data, MaxPixels, new Jpeg2000Options { DiscardLevels = 3 });

        Assert.That((full.Levels, full.Layers), Is.EqualTo((5, 3)));
        Assert.That(full.CodedBytesUsed, Is.EqualTo(full.CodedBytes));
        Assert.That(oneLayer.CodedBytesUsed, Is.LessThan(full.CodedBytes / 2));
        Assert.That(small.CodedBytesUsed, Is.LessThan(full.CodedBytes / 4));
        Assert.That(small.CodedBytes, Is.EqualTo(full.CodedBytes));
    }

    [TestCaseSource(nameof(EightBit))]
    public void ReadsAsOpenJpegReadsIt(string name)
    {
        var path = Path.Combine(Folder(), name + ".jp2");
        if (!File.Exists(path))
        {
            path = Path.ChangeExtension(path, ".j2k");
        }

        var image = Jpeg2000Decoder.Decode(File.ReadAllBytes(path), MaxPixels);
        var expected = File.ReadAllBytes(Path.Combine(Folder(), name + ".rgba8"));
        var actual = Rgba(image);

        Assert.That((image.Width, image.Height), Is.EqualTo((157, 93)));
        var tolerance = name is "rgb-layers" or "rgb-all-modes-layers" || name.Contains("lossy") ? 1 : 0;
        Assert.That(MaxDifference(actual, expected), Is.LessThanOrEqualTo(tolerance));
    }

    [Test]
    public void SixteenBitGrayReadsToEightBits()
    {
        var image = Jpeg2000Decoder.Decode(File.ReadAllBytes(Path.Combine(Folder(), "gray16-lossless.jp2")),
            MaxPixels);
        var deep = File.ReadAllBytes(Path.Combine(Folder(), "gray16-lossless.gray16"));
        var expected = new byte[deep.Length / 2];
        for (var i = 0; i < expected.Length; i++)
        {
            var value = deep[2 * i] | (deep[2 * i + 1] << 8);
            expected[i] = (byte)((value * 255 + 32767) / 65535);
        }

        Assert.That(image.Channels, Is.EqualTo(1));
        Assert.That(image.Samples, Is.EqualTo(expected));
    }

    [Test]
    public void ColorSpaceAndOpacityComeFromTheFile()
    {
        var rgb = Jpeg2000Decoder.Decode(File.ReadAllBytes(Path.Combine(Folder(), "rgb-lossless.jp2")), MaxPixels);
        var gray = Jpeg2000Decoder.Decode(File.ReadAllBytes(Path.Combine(Folder(), "gray-lossless.jp2")), MaxPixels);
        var bare = Jpeg2000Decoder.Decode(File.ReadAllBytes(Path.Combine(Folder(), "rgb-codestream.j2k")),
            MaxPixels);

        Assert.That((rgb.ColorSpace, rgb.ColorChannels, rgb.HasAlpha), Is.EqualTo((Jpeg2000ColorSpace.Srgb, 3, false)));
        Assert.That((gray.ColorSpace, gray.ColorChannels), Is.EqualTo((Jpeg2000ColorSpace.Gray, 1)));
        Assert.That((bare.ColorSpace, bare.ColorChannels), Is.EqualTo((Jpeg2000ColorSpace.Unknown, 3)));
    }

    [Test]
    public void ImageBiggerThanTheLimitIsRefused()
    {
        var data = File.ReadAllBytes(Path.Combine(Folder(), "rgb-lossless.jp2"));

        Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(data, 1000));
    }

    [TestCase("rgb-lossless.jp2")]
    [TestCase("rgb-layers.jp2")]
    [TestCase("rgb-tiles-offset.jp2")]
    [TestCase("rgb-rpcl.jp2")]
    public void MutatedFilesFailOnlyAsPromised(string name)
    {
        var file = File.ReadAllBytes(Path.Combine(Folder(), name));
        var random = new Random(name.Length);
        for (var run = 0; run < 300; run++)
        {
            var mutated = (byte[])file.Clone();
            for (var i = 0; i < 4; i++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            if (run % 10 == 0)
            {
                Array.Resize(ref mutated, random.Next(mutated.Length));
            }

            try
            {
                Jpeg2000Decoder.Decode(mutated, MaxPixels);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
            }
        }
    }

    [Test]
    public void DeeplyNestedBoxesAreRefusedWithoutRecursion()
    {
        var file = new MemoryStream();
        file.Write([0, 0, 0, 12, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A]);
        for (var i = 0; i < 300000; i++)
        {
            file.Write([0, 0, 0, 0, 0x6A, 0x70, 0x32, 0x68]);
        }

        Assert.Throws<InvalidDataException>(() => Jpeg2000Decoder.Decode(file.ToArray(), MaxPixels));
    }

    [TestCase(2000, 1, 1024, 32, 1)]
    [TestCase(1, 1, 64, 0, 65535)]
    public void TinyCodestreamsAskingForHugeWorkStopFast(int tiles, int height, int components, int levels,
        int layers)
    {
        var data = Codestream(tiles, height, components, levels, layers);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Jpeg2000Decoder.Decode(data, MaxPixels);
        }
        catch (InvalidDataException)
        {
        }

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
    }

    private static byte[] Codestream(int tiles, int height, int components, int levels, int layers)
    {
        var output = new MemoryStream();
        void U16(int value) => output.Write([(byte)(value >> 8), (byte)value]);
        void U32(long value) => output.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        U16(0xFF4F);
        U16(0xFF51);
        U16(38 + 3 * components);
        U16(0);
        U32(tiles);
        U32(height);
        U32(0);
        U32(0);
        U32(1);
        U32(height);
        U32(0);
        U32(0);
        U16(components);
        for (var c = 0; c < components; c++)
        {
            output.Write([7, 1, 1]);
        }

        U16(0xFF52);
        U16(12);
        output.WriteByte(0);
        output.WriteByte(0);
        U16(layers);
        output.WriteByte(0);
        output.Write([(byte)levels, 4, 4, 0, 1]);
        U16(0xFF5C);
        U16(3 + 3 * levels + 1);
        output.WriteByte(0x40);
        for (var i = 0; i < 3 * levels + 1; i++)
        {
            output.WriteByte(8 << 3);
        }

        for (var t = 0; t < tiles; t++)
        {
            U16(0xFF90);
            U16(10);
            U16(t);
            U32(14);
            output.Write([0, 1]);
            U16(0xFF93);
        }

        U16(0xFFD9);
        return output.ToArray();
    }

    private static byte[] Rgba(Jpeg2000Image image) => Jpeg2000Bitmap.Rgba(image);

    [Test]
    public void ATileWithFewerLevelsBoundsWhatCanBeLeftOut()
    {
        var data = WithTileLevels(File.ReadAllBytes(Path.Combine(Folder(), "rgb-tiles.jp2")), 2);

        var image = Jpeg2000Decoder.Decode(data, MaxPixels, new Jpeg2000Options { DiscardLevels = 4 });

        Assert.That(image.Levels, Is.EqualTo(2));
        Assert.That((image.Width, image.Height), Is.EqualTo((40, 24)), "ceil(157 / 4), ceil(93 / 4)");
    }

    private static byte[] WithTileLevels(byte[] file, byte levels)
    {
        var cod = IndexOf(file, [0xFF, 0x52], 0);
        var codLength = (file[cod + 2] << 8) | file[cod + 3];
        var marker = file[cod..(cod + 2 + codLength)];
        marker[9] = levels;
        var sot = IndexOf(file, [0xFF, 0x90], cod);
        var at = sot + 12;
        var length = (file[sot + 6] << 24) | (file[sot + 7] << 16) | (file[sot + 8] << 8) | file[sot + 9];
        var spliced = new byte[file.Length + marker.Length];
        Array.Copy(file, spliced, at);
        Array.Copy(marker, 0, spliced, at, marker.Length);
        Array.Copy(file, at, spliced, at + marker.Length, file.Length - at);
        length += marker.Length;
        spliced[sot + 6] = (byte)(length >> 24);
        spliced[sot + 7] = (byte)(length >> 16);
        spliced[sot + 8] = (byte)(length >> 8);
        spliced[sot + 9] = (byte)length;
        var box = IndexOf(spliced, "jp2c"u8.ToArray(), 0) - 4;
        var boxLength = (spliced[box] << 24) | (spliced[box + 1] << 16) | (spliced[box + 2] << 8) | spliced[box + 3];
        if (boxLength != 0)
        {
            boxLength += marker.Length;
            spliced[box] = (byte)(boxLength >> 24);
            spliced[box + 1] = (byte)(boxLength >> 16);
            spliced[box + 2] = (byte)(boxLength >> 8);
            spliced[box + 3] = (byte)boxLength;
        }

        return spliced;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int from)
    {
        for (var i = from; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        throw new InvalidDataException("pattern not found");
    }

    private static int MaxDifference(byte[] a, byte[] b)
    {
        var most = 0;
        for (var i = 0; i < a.Length; i++)
        {
            most = Math.Max(most, Math.Abs(a[i] - b[i]));
        }

        return most;
    }
}
