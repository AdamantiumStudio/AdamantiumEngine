using Adamantium.Pulsar.Decoding;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>Floor 0, which no encoder at hand writes: a setup and a packet made bit by bit, its curve against 6.2.3's
/// formulas.</summary>
[TestFixture]
public class VorbisFloor0Tests
{
    private const int Rate = 44100;
    private const int BarkMapSize = 256;
    private const int AmplitudeBits = 6;
    private const int AmplitudeOffset = 140;
    private const int Amplitude = 40;

    // Of an odd order, the last coefficient of the second vector is read and not used.
    [TestCase(4)]
    [TestCase(3)]
    public void TheCurve_IsTheLspFiltersResponse(int order)
    {
        // A codebook of 2 dimensions, 4 entries of 2 bits; entry e is (e, e + 1) * 0.25 + 0.125.
        var setup = new VorbisBitWriter();
        setup.Write(0x564342, 24);
        setup.Write(2, 16);
        setup.Write(4, 24);
        setup.Write(0, 2);
        for (var entry = 0; entry < 4; entry++)
        {
            setup.Write(1, 5);
        }

        setup.Write(2, 4);
        setup.Write(785u << 21 | 1, 32);
        setup.Write(786u << 21 | 1, 32);
        setup.Write(2, 4);
        setup.Write(0, 1);
        for (var entry = 0; entry < 4; entry++)
        {
            setup.Write((uint)entry, 3);
            setup.Write((uint)entry + 1, 3);
        }

        setup.Write((uint)order, 8);
        setup.Write(Rate, 16);
        setup.Write(BarkMapSize, 16);
        setup.Write(AmplitudeBits, 6);
        setup.Write(AmplitudeOffset, 8);
        setup.Write(0, 4);
        setup.Write(0, 8);
        var setupBytes = setup.ToArray();
        var bits = new VorbisBits(setupBytes);
        VorbisCodebook[] books = [VorbisCodebook.Read(ref bits)];
        var floor = new VorbisFloor0(ref bits, books, 1);

        // Entries 2 and 3, their codewords 10 and 11, the highest bit first: each vector after the one before's last.
        var packet = new VorbisBitWriter();
        packet.Write(Amplitude, AmplitudeBits);
        packet.Write(0, 1);
        packet.Write(1, 1);
        packet.Write(0, 1);
        packet.Write(1, 1);
        packet.Write(1, 1);
        var packetBytes = packet.ToArray();
        var packetBits = new VorbisBits(packetBytes);
        double[] coefficients = [0.625, 0.875, 0.875 + 0.875, 0.875 + 1.125];

        Assert.That(floor.Read(ref packetBits, books, 0), Is.True);
        var spectrum = new float[128];
        Array.Fill(spectrum, 1f);
        floor.Apply(0, spectrum);

        for (var index = 0; index < spectrum.Length; index++)
        {
            var expected = CurveAt(index, spectrum.Length, order, coefficients);
            Assert.That(spectrum[index], Is.EqualTo(expected).Within(expected * 1e-5), $"line {index}");
        }
    }

    // 6.2.3.
    private static double CurveAt(int index, int n, int order, double[] coefficients)
    {
        static double Bark(double x) => 13.1 * Math.Atan(.00074 * x) + 2.24 * Math.Atan(.0000000185 * x * x) + .0001 * x;
        var map = Math.Min(BarkMapSize - 1, Math.Floor(Bark(Rate * index / (2.0 * n)) * (BarkMapSize / Bark(.5 * Rate))));
        var omega = Math.PI * map / BarkMapSize;
        var cos = Math.Cos(omega);
        double p;
        double q;
        if (order % 2 == 1)
        {
            p = 1 - cos * cos;
            for (var j = 0; j <= (order - 3) / 2; j++)
            {
                p *= 4 * Math.Pow(Math.Cos(coefficients[2 * j + 1]) - cos, 2);
            }

            q = 0.25;
            for (var j = 0; j <= (order - 1) / 2; j++)
            {
                q *= 4 * Math.Pow(Math.Cos(coefficients[2 * j]) - cos, 2);
            }
        }
        else
        {
            p = (1 - cos) / 2;
            q = (1 + cos) / 2;
            for (var j = 0; j <= (order - 2) / 2; j++)
            {
                p *= 4 * Math.Pow(Math.Cos(coefficients[2 * j + 1]) - cos, 2);
                q *= 4 * Math.Pow(Math.Cos(coefficients[2 * j]) - cos, 2);
            }
        }

        return Math.Exp(.11512925 * (Amplitude * AmplitudeOffset / ((Math.Pow(2, AmplitudeBits) - 1) * Math.Sqrt(p + q)) -
            AmplitudeOffset));
    }
}
