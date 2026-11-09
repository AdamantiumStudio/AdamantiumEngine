using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Adamantium.Imaging;
using Adamantium.Imaging.Icc;
using Adamantium.Imaging.Jpeg2000;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>Pictures whose file holds an ICC profile, read with the profile applied (see make-images.py) and without.
/// </summary>
[TestFixture]
public class ColorProfileLoadTests
{
    private static readonly BitmapLoadOptions Applied = new() { ApplyColorProfile = true };

    private static string Asset(string name) => TestAssets.Path(Path.Combine("Icc", name));

    private static int Worst(byte[] rgba, byte[] rgb)
    {
        var worst = 0;
        for (var p = 0; p < rgb.Length / 3; p++)
        {
            for (var c = 0; c < 3; c++)
            {
                worst = Math.Max(worst, Math.Abs(rgba[4 * p + c] - rgb[3 * p + c]));
            }
        }

        return worst;
    }

    [TestCase("photo-rgb.png", "rgb-gamma.icc", 1)]
    [TestCase("photo-gray.png", "gray-table.icc", 1)]
    [TestCase("photo-rgb.tif", "rgb-gamma.icc", 1)]
    [TestCase("photo-cmyk.tif", "cmyk-ab.icc", 1)]
    [TestCase("photo-rgb.jpg", "rgb-gamma.icc", 9,
        Description = "libjpeg's samples differ by up to 3, which gamma 2.2 to sRGB stretches in the shadows")]
    [TestCase("photo-cmyk.jpg", "cmyk-ab.icc", 3, Description = "Adobe's inverted inks, and libjpeg's rounding")]
    public void AnAppliedProfileGivesWhatLittleCmsGives(string name, string profile, int tolerance)
    {
        var expected = File.ReadAllBytes(Asset(name + ".srgb"));

        var bitmap = BitmapLoader.Load(Asset(name), Applied);

        Assert.That(bitmap.ColorProfile, Is.Null, "the pixels are sRGB now");
        Assert.That(Worst(bitmap.GetRawPixels(0), expected), Is.LessThanOrEqualTo(tolerance));
    }

    [TestCase("photo-rgb.png", "rgb-gamma.icc")]
    [TestCase("photo-gray.png", "gray-table.icc")]
    [TestCase("photo-rgb.tif", "rgb-gamma.icc")]
    [TestCase("photo-cmyk.tif", "cmyk-ab.icc")]
    [TestCase("photo-rgb.jpg", "rgb-gamma.icc")]
    [TestCase("photo-cmyk.jpg", "cmyk-ab.icc")]
    public void WithoutTheOptionPixelsStayAsStoredAndTheProfileIsGiven(string name, string profile)
    {
        var plain = BitmapLoader.Load(Asset(name));
        var applied = BitmapLoader.Load(Asset(name), Applied);

        Assert.That(plain.ColorProfile, Is.EqualTo(File.ReadAllBytes(Asset(profile))));
        Assert.That(plain.GetRawPixels(0), Is.Not.EqualTo(applied.GetRawPixels(0)));
    }

    [TestCase("photo-rgb.jpg")]
    [TestCase("photo-rgb.png")]
    public void TheProfileAppliesToTheSamplesAsDecoded(string name)
    {
        var stored = BitmapLoader.Load(Asset(name)).GetRawPixels(0);
        var applied = BitmapLoader.Load(Asset(name), Applied).GetRawPixels(0);
        var transform = IccTransform.ToSrgb(IccProfile.Read(File.ReadAllBytes(Asset("rgb-gamma.icc"))),
            IccRenderingIntent.Perceptual);

        var rgb = new float[3];
        for (var p = 0; p < stored.Length / 4; p++)
        {
            transform.Convert([stored[4 * p] / 255f, stored[4 * p + 1] / 255f, stored[4 * p + 2] / 255f], rgb);
            for (var c = 0; c < 3; c++)
            {
                Assert.That(applied[4 * p + c], Is.EqualTo(MathF.Round(rgb[c] * 255)).Within(1), $"pixel {p}");
            }
        }
    }

    [Test]
    public void APngSavedWithItsProfileAppliedCarriesNoProfile()
    {
        var applied = BitmapLoader.Load(Asset("photo-rgb.png"), Applied);
        using var saved = new MemoryStream();
        BitmapLoader.Save(applied, saved, ImageFileType.Png);

        var again = BitmapLoader.Load(new MemoryStream(saved.ToArray()));

        Assert.That(again.ColorProfile, Is.Null);
        Assert.That(again.GetRawPixels(0), Is.EqualTo(applied.GetRawPixels(0)));
    }

    [Test]
    public void AJpegProfileInChunksIsPutTogether()
    {
        var whole = File.ReadAllBytes(Asset("photo-cmyk.jpg"));
        var split = SplitIccSegment(whole);

        var bitmap = BitmapLoader.Load(new MemoryStream(split), Applied);

        Assert.That(BitmapLoader.Load(new MemoryStream(split)).ColorProfile,
            Is.EqualTo(File.ReadAllBytes(Asset("cmyk-ab.icc"))));
        Assert.That(bitmap.GetRawPixels(0), Is.EqualTo(BitmapLoader.Load(Asset("photo-cmyk.jpg"), Applied)
            .GetRawPixels(0)));
    }

    [Test]
    public void AJpegTwoThousandProfileIsApplied()
    {
        var plain = File.ReadAllBytes(TestAssets.Path(Path.Combine("Jpeg2000", "rgba-lossless.jp2")));
        var profile = File.ReadAllBytes(Asset("rgb-gamma.icc"));
        var data = WithColourProfile(plain, profile);
        var transform = IccTransform.ToSrgb(IccProfile.Read(profile), IccRenderingIntent.Perceptual);

        var stored = BitmapLoader.Load(new MemoryStream(data));
        var applied = BitmapLoader.Load(new MemoryStream(data), Applied);

        Assert.That(stored.ColorProfile, Is.EqualTo(profile));
        Assert.That(applied.ColorProfile, Is.Null);
        var before = stored.GetRawPixels(0);
        var after = applied.GetRawPixels(0);
        var rgb = new float[3];
        for (var p = 0; p < before.Length / 4; p++)
        {
            transform.Convert([before[4 * p] / 255f, before[4 * p + 1] / 255f, before[4 * p + 2] / 255f], rgb);
            for (var c = 0; c < 3; c++)
            {
                Assert.That(after[4 * p + c], Is.EqualTo(MathF.Round(rgb[c] * 255)).Within(1));
            }

            Assert.That(after[4 * p + 3], Is.EqualTo(before[4 * p + 3]), "opacity stays");
        }
    }

    [Test]
    public void ATiffsPagesMakeTheirConverterWhenDecodedAndShareIt()
    {
        var path = Asset("pages-rgb.tif");
        var transform = Transform("rgb-gamma.icc");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var applied = BitmapLoader.Load(path, Applied);
        var spent = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(applied.FramesCount, Is.EqualTo(40));
        Assert.That(spent, Is.LessThan(4 << 20), "the first page's converter, not forty");
        var plain = BitmapLoader.Load(path);
        foreach (var frame in new uint[] { 1, 30, 39 })
        {
            AssertThroughTransform(plain.GetRawPixels(frame), applied.GetRawPixels(frame), transform, 0, 1);
        }
    }

    [Test]
    public void AssociatedOpacityIsTakenOutBeforeTheProfile()
    {
        var tiff = File.ReadAllBytes(Asset("photo-premultiplied.tif"));
        var unassociated = (byte[])tiff.Clone();
        var directory = BitConverter.ToInt32(tiff, 4);
        for (var entry = 0; entry < BitConverter.ToUInt16(tiff, directory); entry++)
        {
            var at = directory + 2 + 12 * entry;
            if (BitConverter.ToUInt16(tiff, at) == 338)
            {
                unassociated[at + 8] = 2;
            }
        }

        var stored = BitmapLoader.Load(new MemoryStream(unassociated)).GetRawPixels(0);
        var applied = BitmapLoader.Load(new MemoryStream(tiff), Applied).GetRawPixels(0);

        var transform = Transform("rgb-gamma.icc");
        var rgb = new float[3];
        for (var p = 0; p < stored.Length / 4; p++)
        {
            var opacity = stored[4 * p + 3] / 255f;
            Assert.That(applied[4 * p + 3], Is.EqualTo(stored[4 * p + 3]));
            transform.Convert([stored[4 * p] / 255f / opacity, stored[4 * p + 1] / 255f / opacity,
                stored[4 * p + 2] / 255f / opacity], rgb);
            for (var c = 0; c < 3; c++)
            {
                Assert.That(applied[4 * p + c], Is.EqualTo(MathF.Round(rgb[c] * 255)).Within(1), $"pixel {p}");
            }
        }
    }

    [Test]
    public void PremultipliedJpegTwoThousandColorsAreTakenOutBeforeTheProfile()
    {
        var jp2 = File.ReadAllBytes(TestAssets.Path(Path.Combine("Jpeg2000", "rgba-lossless.jp2")));
        var header = Find(jp2, "jp2h", 0, jp2.Length);
        var definitions = Find(jp2, "cdef", header + 8, header + ReadUInt32(jp2, header));
        jp2[definitions + 10 + 6 * 3 + 3] = 2;
        var data = WithColourProfile(jp2, File.ReadAllBytes(Asset("rgb-gamma.icc")));

        var plain = BitmapLoader.Load(new MemoryStream(data)).GetRawPixels(0);
        var applied = BitmapLoader.Load(new MemoryStream(data), Applied).GetRawPixels(0);

        AssertThroughTransform(plain, applied, Transform("rgb-gamma.icc"), 0, 1);
    }

    [TestCase("Lab ", null)]
    [TestCase("CMY ", null)]
    [TestCase(null, "link")]
    [TestCase(null, "abst")]
    public void AProfileOfAnotherColorSpaceOrClassIsLeftAlone(string space, string profileClass)
    {
        var profile = File.ReadAllBytes(Asset("rgb-lut16.icc"));
        if (space != null)
        {
            Encoding.ASCII.GetBytes(space).CopyTo(profile, 16);
        }

        if (profileClass != null)
        {
            Encoding.ASCII.GetBytes(profileClass).CopyTo(profile, 12);
        }

        Assert.That(IccPixels.Converter(profile, 3, 100), Is.Null);
        Assert.That(IccPixels.Converter(File.ReadAllBytes(Asset("rgb-lut16.icc")), 3, 100), Is.Not.Null);
    }

    private static IccTransform Transform(string name) =>
        IccTransform.ToSrgb(IccProfile.Read(File.ReadAllBytes(Asset(name))), IccRenderingIntent.Perceptual);

    private static void AssertThroughTransform(byte[] plain, byte[] applied, IccTransform transform, int fromOpacity,
        int tolerance)
    {
        var rgb = new float[3];
        for (var p = 0; p < plain.Length / 4; p++)
        {
            Assert.That(applied[4 * p + 3], Is.EqualTo(plain[4 * p + 3]), "opacity stays");
            if (plain[4 * p + 3] < fromOpacity)
            {
                continue;
            }

            transform.Convert([plain[4 * p] / 255f, plain[4 * p + 1] / 255f, plain[4 * p + 2] / 255f], rgb);
            for (var c = 0; c < 3; c++)
            {
                Assert.That(applied[4 * p + c], Is.EqualTo(MathF.Round(rgb[c] * 255)).Within(tolerance),
                    $"pixel {p}, channel {c}");
            }
        }
    }

    [TestCase("rgb-gamma.icc", 3, 2)]
    [TestCase("cmyk-ab.icc", 4, 8)]
    public void TheGridOfManyColorsStaysCloseToTheTransform(string name, int channels, int tolerance)
    {
        var transform = IccTransform.ToSrgb(IccProfile.Read(File.ReadAllBytes(Asset(name))),
            IccRenderingIntent.Perceptual);
        var grid = new IccPixelConverter(transform, long.MaxValue);
        var random = new Random(3);
        var device = new float[channels];
        float[] exact = new float[3], interpolated = new float[3];
        var worst = 0f;
        for (var i = 0; i < 20000; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                device[c] = random.Next(256) / 255f;
            }

            transform.Convert(device, exact);
            grid.Convert(device, interpolated);
            for (var c = 0; c < 3; c++)
            {
                worst = Math.Max(worst, MathF.Abs(MathF.Round(exact[c] * 255) - MathF.Round(interpolated[c] * 255)));
            }
        }

        Assert.That(worst, Is.LessThanOrEqualTo(tolerance));
    }

    [Test]
    public void SixteenBitPixelsConvertAsEightBitOnes()
    {
        var transform = IccTransform.ToSrgb(IccProfile.Read(File.ReadAllBytes(Asset("rgb-gamma.icc"))),
            IccRenderingIntent.Perceptual);
        var converter = new IccPixelConverter(transform, 0);
        byte[] narrow = [10, 200, 90, 255, 250, 3, 128, 7];
        var wide = new byte[narrow.Length * 2];
        for (var i = 0; i < narrow.Length; i++)
        {
            BitConverter.TryWriteBytes(wide.AsSpan(2 * i), (ushort)(narrow[i] * 257));
        }

        IccPixels.InPlace(converter, narrow, false);
        IccPixels.InPlace(converter, wide, true);

        for (var i = 0; i < narrow.Length; i++)
        {
            Assert.That(BitConverter.ToUInt16(wide, 2 * i) / 257.0, Is.EqualTo(narrow[i]).Within(1), $"sample {i}");
        }
    }

    private static byte[] SplitIccSegment(byte[] jpeg)
    {
        for (var at = 2; at + 4 < jpeg.Length;)
        {
            var length = jpeg[at + 2] << 8 | jpeg[at + 3];
            if (jpeg[at + 1] == 0xE2 && Encoding.ASCII.GetString(jpeg, at + 4, 11) == "ICC_PROFILE")
            {
                var data = jpeg[(at + 4 + 14)..(at + 2 + length)];
                var half = data.Length / 2;
                var result = new List<byte>(jpeg[..at]);
                result.AddRange(Segment(2, 2, data[half..]));
                result.AddRange(Segment(1, 2, data[..half]));
                result.AddRange(jpeg[(at + 2 + length)..]);
                return result.ToArray();
            }

            at += 2 + length;
        }

        throw new InvalidDataException("No ICC segment.");
    }

    private static byte[] Segment(byte sequence, byte count, byte[] data)
    {
        var length = 2 + 14 + data.Length;
        var segment = new List<byte> { 0xFF, 0xE2, (byte)(length >> 8), (byte)length };
        segment.AddRange("ICC_PROFILE\0"u8.ToArray());
        segment.Add(sequence);
        segment.Add(count);
        segment.AddRange(data);
        return segment.ToArray();
    }

    private static byte[] WithColourProfile(byte[] jp2, byte[] profile)
    {
        var header = Find(jp2, "jp2h", 0, jp2.Length);
        var headerLength = ReadUInt32(jp2, header);
        var colour = Find(jp2, "colr", header + 8, header + headerLength);
        var colourLength = ReadUInt32(jp2, colour);
        var box = new byte[8 + 3 + profile.Length];
        WriteUInt32(box, 0, box.Length);
        Encoding.ASCII.GetBytes("colr").CopyTo(box, 4);
        box[8] = 2;
        profile.CopyTo(box, 11);
        var result = new List<byte>(jp2[..colour]);
        result.AddRange(box);
        result.AddRange(jp2[(colour + colourLength)..]);
        var bytes = result.ToArray();
        WriteUInt32(bytes, header, headerLength - colourLength + box.Length);
        return bytes;
    }

    private static int Find(byte[] data, string type, int from, int end)
    {
        for (var at = from; at + 8 <= end; at += ReadUInt32(data, at))
        {
            if (Encoding.ASCII.GetString(data, at + 4, 4) == type)
            {
                return at;
            }
        }

        throw new InvalidDataException("No " + type + " box.");
    }

    private static int ReadUInt32(byte[] data, int at) =>
        data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];

    private static void WriteUInt32(byte[] data, int at, int value)
    {
        data[at] = (byte)(value >> 24);
        data[at + 1] = (byte)(value >> 16);
        data[at + 2] = (byte)(value >> 8);
        data[at + 3] = (byte)value;
    }
}
