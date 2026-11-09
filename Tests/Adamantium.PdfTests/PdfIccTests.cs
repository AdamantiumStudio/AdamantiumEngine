using System;
using System.IO;
using System.Text;
using Adamantium.Imaging.Icc;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Images;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;
using Adamantium.Pdf.Resources;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Colors shown through ICC profiles: those of ICCBased spaces, the output intent's for device colors, the
/// one a JPEG 2000 image holds - relative colorimetric with black point compensation.</summary>
public class PdfIccTests
{
    private static byte[] Profile(string name) =>
        File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Icc", name));

    private static IccTransform Transform(string name) =>
        IccTransform.ToSrgb(IccProfile.Read(Profile(name)), IccRenderingIntent.RelativeColorimetric, true);

    private static PdfDocument Document(string catalog, params byte[][] objects)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R " + catalog + " >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        foreach (var body in objects)
        {
            builder.Add(body);
        }

        return PdfDocument.Open(builder.Build("/Root 1 0 R"));
    }

    private static PdfColorSpace Space(PdfDocument document, string written, string resources = "<< >>")
    {
        var value = new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(written))).ReadObject();
        var dictionary = (PdfDictionary)new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(resources)))
            .ReadObject();
        return PdfColorSpaces.Create(value, new PdfResources(dictionary, document), document);
    }

    private static double[] Rgb(PdfColorSpace space, params double[] components)
    {
        var rgb = new double[3];
        space.ToRgb(components, rgb);
        return rgb;
    }

    private static double[] Expected(IccTransform transform, params float[] device)
    {
        var rgb = new float[3];
        transform.Convert(device, rgb);
        return [rgb[0], rgb[1], rgb[2]];
    }

    [Test]
    public void IccBasedGoesThroughItsProfile()
    {
        var document = Document("", PdfFileBuilder.Stream("/N 4 /Alternate /DeviceCMYK", Profile("cmyk-ab.icc")));
        var transform = Transform("cmyk-ab.icc");

        var space = (PdfIccBasedColorSpace)Space(document, "[/ICCBased 3 0 R]");

        Assert.That(space.IsManaged, Is.True);
        Assert.That(Rgb(space, 0.2, 0.5, 0.1, 0.3), Is.EqualTo(Expected(transform, 0.2f, 0.5f, 0.1f, 0.3f))
            .Within(1e-6));
        Assert.That(Rgb(space, 0, 0, 0, 1), Is.EqualTo(Expected(transform, 0, 0, 0, 1)).Within(1e-6));
        Assert.That(Rgb(space, 0, 0, 0, 1), Is.Not.EqualTo(Rgb(PdfDeviceCmykColorSpace.Instance, 0, 0, 0, 1)));
    }

    [Test]
    public void RangeIsBroughtToTheProfilesZeroToOne()
    {
        var document = Document("",
            PdfFileBuilder.Stream("/N 4 /Range [0 2 0 2 0 2 0 2]", Profile("cmyk-ab.icc")));
        var transform = Transform("cmyk-ab.icc");

        var space = Space(document, "[/ICCBased 3 0 R]");

        Assert.That(Rgb(space, 0.4, 1, 0.2, 0.6), Is.EqualTo(Expected(transform, 0.2f, 0.5f, 0.1f, 0.3f))
            .Within(1e-6));
    }

    [Test]
    public void ARangeWhoseHighIsNotAboveItsLowIsLeftOut()
    {
        var document = Document("",
            PdfFileBuilder.Stream("/N 4 /Range [1 0 0 1 0 1 0 1]", Profile("cmyk-ab.icc")));
        var transform = Transform("cmyk-ab.icc");

        var space = Space(document, "[/ICCBased 3 0 R]");

        Assert.That(space.DefaultDecode(8), Is.EqualTo(new double[] { 0, 1, 0, 1, 0, 1, 0, 1 }));
        Assert.That(Rgb(space, 0.2, 0.5, 0.1, 0.3), Is.EqualTo(Expected(transform, 0.2f, 0.5f, 0.1f, 0.3f))
            .Within(1e-6));
    }

    [Test]
    public void AProfileOfOtherComponentsOrUnreadableLeavesTheAlternate()
    {
        var document = Document("", PdfFileBuilder.Stream("/N 3 /Alternate /DeviceRGB", Profile("cmyk-ab.icc")),
            PdfFileBuilder.Stream("/N 4", Profile("cmyk-ab.icc")[..200]));

        var other = (PdfIccBasedColorSpace)Space(document, "[/ICCBased 3 0 R]");
        var cut = (PdfIccBasedColorSpace)Space(document, "[/ICCBased 4 0 R]");

        Assert.That(other.IsManaged, Is.False);
        Assert.That(Rgb(other, 0.1, 0.2, 0.3), Is.EqualTo(new[] { 0.1, 0.2, 0.3 }).Within(1e-12));
        Assert.That(cut.IsManaged, Is.False);
        Assert.That(Rgb(cut, 0, 1, 0.5, 0.2), Is.EqualTo(new[] { 0.8, 0, 0.3 }).Within(1e-12));
    }

    [Test]
    public void DeviceColorsOfTheOutputIntentsComponentsGoThroughItsProfile()
    {
        var document = Document(
            "/OutputIntents [<< /Type /OutputIntent /S /GTS_PDFX /DestOutputProfile 3 0 R >>]",
            PdfFileBuilder.Stream("/N 4", Profile("cmyk-ab.icc")));
        var transform = Transform("cmyk-ab.icc");

        var cmyk = Space(document, "/DeviceCMYK");
        var rgb = Space(document, "/DeviceRGB");
        var defaulted = Space(document, "/DeviceCMYK", "<< /ColorSpace << /DefaultCMYK [/ICCBased 3 0 R] >> >>");

        Assert.That(cmyk, Is.TypeOf<PdfIccBasedColorSpace>());
        Assert.That(Rgb(cmyk, 0.2, 0.5, 0.1, 0.3), Is.EqualTo(Expected(transform, 0.2f, 0.5f, 0.1f, 0.3f))
            .Within(1e-6));
        Assert.That(rgb, Is.SameAs(PdfDeviceRgbColorSpace.Instance), "the profile is CMYK");
        Assert.That(((PdfIccBasedColorSpace)defaulted).IsManaged, Is.True);
    }

    [TestCase(4, 1, 0, Description = "fewer pixels than grid nodes: each pixel through the profile")]
    [TestCase(300, 300, 8, Description = "more: through the grid - littleCMS's own 8-bit grid is off by 17 here")]
    public void ImagesInAManagedSpaceAreReadAsThroughTheProfile(int width, int height, int tolerance)
    {
        var random = new Random(7);
        var samples = new byte[width * height * 4];
        random.NextBytes(samples);
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add(PdfFileBuilder.Stream("/N 4", Profile("cmyk-ab.icc")));
        var image = builder.Add(PdfFileBuilder.Stream(
            $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 " +
            "/ColorSpace [/ICCBased 3 0 R]", samples));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var transform = Transform("cmyk-ab.icc");

        var bitmap = PdfImageDecoder.Decode((PdfStream)document.GetObject(image),
            new PdfResources(new PdfDictionary(), document), document);

        var worst = 0.0;
        var at = "";
        for (var p = 0; p < width * height; p++)
        {
            var expected = Expected(transform, samples[4 * p] / 255f, samples[4 * p + 1] / 255f,
                samples[4 * p + 2] / 255f, samples[4 * p + 3] / 255f);
            for (var c = 0; c < 3; c++)
            {
                var difference = Math.Abs(bitmap.Rgba[4 * p + c] - Math.Round(expected[c] * 255));
                if (difference > worst)
                {
                    worst = difference;
                    at = $"{string.Join(",", samples[(4 * p)..(4 * p + 4)])} gave " +
                         $"{string.Join(",", bitmap.Rgba[(4 * p)..(4 * p + 3)])}, the profile " +
                         string.Join(",", Array.ConvertAll(expected, v => Math.Round(v * 255)));
                }
            }
        }

        Assert.That(worst, Is.LessThanOrEqualTo(tolerance), at);
    }

    [Test]
    public void JpegTwoThousandWithNoColorSpaceIsShownThroughTheProfileItHolds()
    {
        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "Jpeg2000");
        var plain = File.ReadAllBytes(Path.Combine(folder, "rgba-lossless.jp2"));
        var colors = File.ReadAllBytes(Path.Combine(folder, "rgba-lossless.rgba8"));
        var data = WithProfile(plain, Profile("rgb-gamma.icc"));
        var transform = Transform("rgb-gamma.icc");
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var image = builder.Add(PdfFileBuilder.Stream(
            "/Type /XObject /Subtype /Image /Width 157 /Height 93 /Filter /JPXDecode /SMaskInData 0", data));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));

        var bitmap = PdfImageDecoder.Decode((PdfStream)document.GetObject(image),
            new PdfResources(new PdfDictionary(), document), document);

        var changed = false;
        for (var i = 0; i < colors.Length; i += 4)
        {
            var expected = Expected(transform, colors[i] / 255f, colors[i + 1] / 255f, colors[i + 2] / 255f);
            for (var c = 0; c < 3; c++)
            {
                Assert.That(bitmap.Rgba[i + c], Is.EqualTo(Math.Round(expected[c] * 255)));
                changed |= bitmap.Rgba[i + c] != colors[i + c];
            }
        }

        Assert.That(changed, Is.True, "the profile is not sRGB");
    }

    private static byte[] WithProfile(byte[] jp2, byte[] profile)
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

        var result = PdfFileBuilder.Concat(jp2[..colour], box, jp2[(colour + colourLength)..]);
        WriteUInt32(result, header, headerLength - colourLength + box.Length);
        return result;
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
