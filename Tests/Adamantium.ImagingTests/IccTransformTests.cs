using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adamantium.Imaging.Icc;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>Profiles written from ICC.1 (see make-profiles.py), their colors as sRGB against what littleCMS makes of
/// them, for every intent and for relative colorimetric with black point compensation.</summary>
[TestFixture]
public class IccTransformTests
{
    private static string Folder() => TestAssets.Path("Icc");

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var file in Directory.GetFiles(Folder(), "*.icc").Order())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            for (var intent = 0; intent < 4; intent++)
            {
                yield return new TestCaseData(name, (IccRenderingIntent)intent, false).SetName($"{name} {intent}");
            }

            yield return new TestCaseData(name, IccRenderingIntent.RelativeColorimetric, true).SetName($"{name} 1bpc");
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void ConvertsAsLittleCmsConverts(string name, IccRenderingIntent intent, bool compensated)
    {
        var profile = IccProfile.Read(File.ReadAllBytes(Path.Combine(Folder(), name + ".icc")));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), name + ".json")));
        var inputs = json.RootElement.GetProperty("inputs").EnumerateArray()
            .Select(e => e.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();
        var outputs = json.RootElement.GetProperty("outputs").GetProperty(compensated ? "1bpc" : ((int)intent).ToString()).EnumerateArray()
            .Select(e => e.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();

        var transform = compensated ? IccTransform.ToSrgb(profile, intent, true) : IccTransform.ToSrgb(profile, intent);

        var device = new float[profile.Components];
        var rgb = new float[3];
        var worst = 0;
        var at = -1;
        for (var i = 0; i < inputs.Length; i++)
        {
            for (var c = 0; c < device.Length; c++)
            {
                device[c] = inputs[i][c] / 255f;
            }

            transform.Convert(device, rgb);
            for (var c = 0; c < 3; c++)
            {
                var difference = Math.Abs((int)MathF.Round(rgb[c] * 255) - outputs[i][c]);
                if (difference > worst)
                {
                    (worst, at) = (difference, i);
                }
            }
        }

        Assert.That(worst, Is.LessThanOrEqualTo(1),
            at < 0 ? "" : $"input {string.Join(",", inputs[at])} gave {string.Join(",", outputs[at])} in littleCMS");
    }

    [Test]
    public void TheHeaderSaysWhatTheProfileIs()
    {
        var profile = IccProfile.Read(File.ReadAllBytes(Path.Combine(Folder(), "cmyk-ab.icc")));

        Assert.That((profile.ColorSpace, profile.Components, profile.ConnectionSpace),
            Is.EqualTo((IccColorSpace.Cmyk, 4, IccColorSpace.Lab)));
        Assert.That((profile.Version.Major, profile.Class), Is.EqualTo((4, "prtr")));
    }

    [Test]
    public void BytesThatAreNoProfileAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => IccProfile.Read(new byte[200]));
        Assert.Throws<InvalidDataException>(() => IccProfile.Read(new byte[10]));
    }

    [Test]
    public void MutatedProfilesFailOnlyAsPromised()
    {
        var file = File.ReadAllBytes(Path.Combine(Folder(), "cmyk-ab.icc"));
        var random = new Random(9);
        var device = new float[4];
        var rgb = new float[3];
        for (var run = 0; run < 2000; run++)
        {
            var mutated = (byte[])file.Clone();
            for (var i = 0; i < 3; i++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            try
            {
                var transform = IccTransform.ToSrgb(IccProfile.Read(mutated), IccRenderingIntent.Perceptual);
                transform.Convert(device, rgb);
            }
            catch (InvalidDataException)
            {
            }
        }
    }
}
