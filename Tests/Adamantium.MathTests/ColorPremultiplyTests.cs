using System;
using System.Linq;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>Colors premultiplied by their alpha: red, green and blue each times alpha over 255, rounded, alpha kept - one
/// color, and rows of them with blocks wholly clear, wholly opaque and mixed, and the tail past the last block.</summary>
[TestFixture]
public class ColorPremultiplyTests
{
    [Test]
    public void EveryChannelAndAlpha_IsRoundedAsChannelTimesAlphaOver255()
    {
        for (var alpha = 0; alpha < 256; alpha++)
        {
            for (var channel = 0; channel < 256; channel++)
            {
                var color = new Color((byte)channel, (byte)(255 - channel), (byte)channel, (byte)alpha);
                var expected = new Color(Exact(channel, alpha), Exact(255 - channel, alpha), Exact(channel, alpha), (byte)alpha);

                Assert.That(color.ToPremultiplied(), Is.EqualTo(expected), $"channel {channel}, alpha {alpha}");
            }
        }
    }

    [Test]
    public void ARow_IsEachColorPremultiplied_InBlocksAndTheTail()
    {
        Color[] straight =
        [
            new(255, 255, 255, 0), new(1, 2, 3, 0), default, default, default, default, default, default,
            new(16, 32, 48, 255), new(64, 80, 96, 255), new(112, 128, 144, 255), new(160, 176, 192, 255),
            new(0, 0, 0, 255), new(255, 255, 255, 255), new(1, 2, 3, 255), new(4, 5, 6, 255),
            new(255, 128, 0, 128), new(16, 32, 48, 255), new(255, 255, 255, 0), new(255, 255, 255, 64),
            default, default, default, default,
            new(255, 255, 255, 128), new(171, 205, 239, 0), new(17, 34, 51, 255)
        ];
        var premultiplied = new Color[straight.Length];

        Color.Premultiply(straight, premultiplied);

        Assert.That(premultiplied, Is.EqualTo(straight.Select(color => color.ToPremultiplied()).ToArray()));
    }

    [Test]
    public void ABgraRow_IsPremultipliedAsTheSameColors()
    {
        ColorBGRA[] straight = [new(255, 128, 0, 128), new(10, 20, 30, 0), new(10, 20, 30, 255), new(200, 100, 50, 77)];
        var premultiplied = new ColorBGRA[straight.Length];

        ColorBGRA.Premultiply(straight, premultiplied);

        Assert.That(premultiplied.Select(color => (Color)color),
            Is.EqualTo(straight.Select(color => ((Color)color).ToPremultiplied())));
    }

    [Test]
    public void AShorterDestination_IsRefused() =>
        Assert.Throws<ArgumentException>(() => Color.Premultiply(new Color[4], new Color[3]));

    private static byte Exact(int channel, int alpha) =>
        (byte)Math.Round(channel * alpha / 255.0, MidpointRounding.AwayFromZero);
}
