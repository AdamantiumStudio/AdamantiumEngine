using Adamantium.Pulsar.Decoding;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>The inverse MDCT by its FFT against the sums of its definition.</summary>
[TestFixture]
public class VorbisTransformTests
{
    [TestCase(64)]
    [TestCase(256)]
    public void TheDct4_IsTheSumsOfItsDefinition(int size)
    {
        var random = new Random(size);
        var lines = new float[size / 2];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = random.NextSingle() * 2 - 1;
        }

        var dct = new VorbisTransform(size).Dct4(lines).ToArray();
        var half = lines.Length;
        for (var m = 0; m < half; m++)
        {
            var sum = 0.0;
            for (var k = 0; k < half; k++)
            {
                sum += lines[k] * Math.Cos(Math.PI / half * (m + 0.5) * (k + 0.5));
            }

            Assert.That(dct[m], Is.EqualTo(sum).Within(1e-4), $"line {m}");
        }
    }

    [TestCase(64)]
    [TestCase(256)]
    [TestCase(2048)]
    [TestCase(8192)]
    public void TheBlock_IsTheSumsOfItsDefinition(int size)
    {
        var random = new Random(size);
        var lines = new float[size / 2];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = random.NextSingle() * 2 - 1;
        }

        var samples = new float[size];
        new VorbisTransform(size).Inverse(lines, samples);

        var most = 0.0;
        for (var at = 0; at < size; at += size >= 2048 ? 7 : 1)
        {
            var sum = 0.0;
            for (var k = 0; k < lines.Length; k++)
            {
                sum += lines[k] * Math.Cos(2 * Math.PI / size * (at + 0.5 + size / 4.0) * (k + 0.5));
            }

            most = Math.Max(most, Math.Abs(sum - samples[at]));
        }

        // Of sums of n/2 values of up to 1: a few millionths of them.
        Assert.That(most, Is.LessThan(size * 2e-6), $"block of {size}");
    }
}
