using System.Buffers.Binary;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>Ogg Opus files of libopus, in each of its modes, against libopus's decoding of each, kept as 16-bit FLAC.
/// The codec itself is checked against the specification's test vectors apart, not here: they are 121 MB.</summary>
[TestFixture]
public class OpusTests
{
    private const float Within = 2f / 32768;

    // CELT; SILK of 20, 40 and 60 ms; hybrid; CELT of 2.5 ms frames; 5.1 of mapping family 1.
    [TestCase("music96", 2)]
    [TestCase("voice12", 1)]
    [TestCase("voice40", 1)]
    [TestCase("long9", 1)]
    [TestCase("hybrid24", 2)]
    [TestCase("short64", 2)]
    [TestCase("surround", 6)]
    public void WhatTheReferenceDecodes_IsDecoded(string name, int channels)
    {
        var sound = SoundFile.Read(Read(name + ".opus"));
        var expected = SoundFile.Read(Read(name + ".flac"));

        Assert.That(sound.Format, Is.EqualTo(new AudioFormat(48000, channels)));
        Assert.That(sound.Frames, Is.EqualTo(expected.Frames));
        var most = 0f;
        var at = 0;
        var heard = sound.AsSpan();
        var reference = expected.AsSpan();
        for (var index = 0; index < heard.Length; index++)
        {
            var difference = Math.Abs(heard[index] - reference[index]);
            if (difference > most)
            {
                most = difference;
                at = index;
            }
        }

        Assert.That(most, Is.LessThanOrEqualTo(Within), $"frame {at / channels}, channel {at % channels}");
    }

    [TestCase("pages32")]
    [TestCase("music96")]
    [TestCase("surround")]
    [TestCase("long9")]
    public void Sought_ItReadsOnAsFromTheBeginning(string name)
    {
        var whole = SoundFile.Read(Read(name + ".opus"));
        var channels = whole.Format.Channels;
        using var decoder = SoundFile.Open(new MemoryStream(Read(name + ".opus")));
        var random = new Random(1);
        var samples = new float[700 * channels];
        int[] frames = [0, 1, 959, 960, whole.Frames - 1, whole.Frames, 0, .. Enumerable.Range(0, 60).Select(_ => random.Next(whole.Frames))];
        var isOnePage = OggPages.Of(Read(name + ".opus")).Count <= 3;
        var worst = 0f;
        foreach (var frame in frames)
        {
            decoder.Seek(frame);
            var got = decoder.Read(samples);

            Assert.That(got, Is.EqualTo(Math.Min(700, whole.Frames - frame)), $"frame {frame}");
            var expected = whole.AsSpan().Slice(frame * channels, got * channels);
            for (var index = 0; index < got * channels; index++)
            {
                worst = Math.Max(worst, Math.Abs(samples[index] - expected[index]));
            }
        }

        // From a page before: the decoder's state 80 ms on is near that of decoding from the beginning, not the same,
        // and what it gives within 1% of full scale; from the first page it is the same.
        Assert.That(worst, isOnePage ? Is.Zero : Is.LessThan(.01f), name);
    }

    // Its output gain: 1541 of Q7.8, 6.02 dB, about twice as loud.
    [Test]
    public void TheOutputGain_IsApplied()
    {
        var file = Read("music96.opus");
        var quiet = SoundFile.Read(file);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(OggPages.Of(file)[0] + 28 + 16), 1541);
        OggPages.Seal(file, 0);

        var loud = SoundFile.Read(file);

        var factor = (float)Math.Pow(10, 1541 / (20.0 * 256));
        for (var index = 0; index < quiet.AsSpan().Length; index++)
        {
            Assert.That(loud.AsSpan()[index], Is.EqualTo(factor * quiet.AsSpan()[index]).Within(1e-5f), $"sample {index}");
        }
    }

    // Its granules 100000 on, as a stream cut from another's middle: read from its first sample all the same.
    [Test]
    public void ALaterStream_IsItsSamples()
    {
        var file = Read("pages32.opus");
        var whole = SoundFile.Read(file);
        foreach (var page in OggPages.Of(file).Skip(2))
        {
            OggPages.SetGranule(file, page, OggPages.GranuleOf(file, page) + 100000);
            OggPages.Seal(file, page);
        }

        var later = SoundFile.Read(file);

        Assert.That(later.AsSpan().SequenceEqual(whole.AsSpan()), Is.True);
    }

    // A page spoilt, its checksum not its own: what it held is silence, of the length the granules say; what is after
    // it as before, once the decoder is past what it had of the page before. Its lacing spoilt too, at 60 bytes in:
    // the page after it is found all the same. The one before the last spoilt: the last, whose granule cuts the end,
    // is placed where the page before ended.
    [TestCase(2)]
    [TestCase(3)]
    public void ADamagedPage_IsSilenceAndTheRestIsRead(int page)
    {
        var file = Read("pages32.opus");
        var whole = SoundFile.Read(file);
        file[OggPages.Of(file)[page] + 60] ^= 0x55;

        var sound = SoundFile.Read(file);

        Assert.That(sound.Frames, Is.EqualTo(whole.Frames));
        var heard = sound.AsSpan().ToArray();
        var expected = whole.AsSpan().ToArray();
        var differing = Enumerable.Range(0, sound.Frames).Where(frame => Math.Abs(heard[frame] - expected[frame]) > .01f).ToArray();
        Assert.That(differing, Is.Not.Empty);
        // A page of a second, and what the decoder takes to come back after it.
        Assert.That(differing[^1] - differing[0], Is.LessThan(48000 + 9600));
    }

    [Test]
    public void Streamed_ItIsReadToo()
    {
        var sound = StreamedSound.Open(new SoundMemoryStorage(Read("pages32.opus")));

        Assert.That(sound.Frames, Is.EqualTo(SoundFile.Read(Read("pages32.opus")).Frames));
    }

    // Of a version past those read, of a mapping family there is not, of no channels: refused.
    [TestCase(8, 16)]
    [TestCase(18, 2)]
    [TestCase(9, 0)]
    public void AHeaderNotRead_IsRefused(int at, int value)
    {
        var file = Read("music96.opus");
        file[OggPages.Of(file)[0] + 28 + at] = (byte)value;
        OggPages.Seal(file, 0);

        Assert.That(() => SoundFile.Read(file), Throws.InstanceOf<InvalidDataException>());
    }

    // Any byte of a file spoilt, or the file cut anywhere: refused, or read, nothing that is no number.
    [TestCase("pages32")]
    [TestCase("surround")]
    public void ASpoiltFile_IsRefusedOrRead(string name)
    {
        var file = Read(name + ".opus");
        var random = new Random(2);
        for (var round = 0; round < 300; round++)
        {
            var spoilt = (byte[])file.Clone();
            for (var count = 0; count < 1 + round % 8; count++)
            {
                spoilt[random.Next(spoilt.Length)] = (byte)random.Next(256);
            }

            ReadOrRefuse(round % 5 == 0 ? spoilt[..random.Next(spoilt.Length)] : spoilt);
        }

        static void ReadOrRefuse(byte[] bytes)
        {
            try
            {
                var sound = SoundFile.Read(bytes);
                foreach (var sample in sound.AsSpan())
                {
                    Assert.That(float.IsFinite(sample), Is.True);
                }
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [TestCase("pages32")]
    [TestCase("surround")]
    [TestCase("hybrid24")]
    public void Decoding_AllocatesNothing(string name)
    {
        using var decoder = SoundFile.Open(new MemoryStream(Read(name + ".opus")));
        var samples = new float[4096 * decoder.Format.Channels];
        decoder.Read(samples);
        var least = long.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            decoder.Seek(0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            while (decoder.Read(samples) > 0)
            {
            }

            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.That(least, Is.Zero);
    }

    private static byte[] Read(string name)
    {
        using var resource = typeof(OpusTests).Assembly.GetManifestResourceStream("opus." + name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
