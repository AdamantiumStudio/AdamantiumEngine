using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>MP3 files of LAME and shine, against ffmpeg's decoding of each: kept as 16-bit FLAC, so within half a step of
/// 16 bits, and what two decoders' rounding leaves.</summary>
[TestFixture]
public class Mp3Tests
{
    private const float Within = 1.2f / 32768;

    [TestCase("cbr128", 44100, 2)]
    [TestCase("vbr48", 48000, 2)]
    [TestCase("mono32", 32000, 1)]
    [TestCase("stereo44", 44100, 2)]
    [TestCase("lower22", 22050, 2)]
    [TestCase("lowest8", 8000, 1)]
    [TestCase("shine44", 44100, 2)]
    // cbr128's joint stereo frames with intensity on as well: no coder at hand writes it.
    [TestCase("intensity44", 44100, 2)]
    public void WhatFfmpegDecodes_IsDecoded(string name, int rate, int channels)
    {
        var sound = SoundFile.Read(Read(name + ".mp3"));
        var expected = SoundFile.Read(Read(name + ".flac"));

        Assert.That(sound.Format, Is.EqualTo(new AudioFormat(rate, channels)));
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

        Assert.That(most, Is.LessThanOrEqualTo(Within), $"frame {at / channels}");
    }

    [TestCase("cbr128")]
    [TestCase("vbr48")]
    [TestCase("lower22")]
    [TestCase("lowest8")]
    public void Sought_ItReadsOnAsFromTheBeginning(string name)
    {
        var whole = SoundFile.Read(Read(name + ".mp3"));
        var channels = whole.Format.Channels;
        using var decoder = SoundFile.Open(new MemoryStream(Read(name + ".mp3")));
        var random = new Random(1);
        var samples = new float[700 * channels];
        int[] frames = [0, 1, 575, 576, 1151, 1152, whole.Frames - 1, whole.Frames, 0, .. Enumerable.Range(0, 100).Select(_ => random.Next(whole.Frames))];
        foreach (var frame in frames)
        {
            decoder.Seek(frame);
            var got = decoder.Read(samples);

            Assert.That(got, Is.EqualTo(Math.Min(700, whole.Frames - frame)), $"frame {frame}");
            Assert.That(samples.AsSpan(0, got * channels).SequenceEqual(whole.AsSpan().Slice(frame * channels, got * channels)),
                Is.True, $"frame {frame}");
        }
    }

    [Test]
    public void Streamed_ItIsReadToo()
    {
        var sound = StreamedSound.Open(new SoundMemoryStorage(Read("cbr128.mp3")));

        Assert.That(sound.Frames, Is.EqualTo(SoundFile.Read(Read("cbr128.mp3")).Frames));
    }

    [Test]
    public void TagsBefore_AreSkipped()
    {
        var file = Read("cbr128.mp3");
        // An ID3v2.4 tag of 300 bytes, its size in 7 bits a byte, and another after it.
        byte[] tag = [.. "ID3"u8, 4, 0, 0, 0, 0, 2, 44, .. new byte[300]];
        var tagged = SoundFile.Read([.. tag, .. tag, .. file]);

        Assert.That(tagged.AsSpan().SequenceEqual(SoundFile.Read(file).AsSpan()), Is.True);
    }

    [Test]
    public void ZerosBefore_AreSkipped()
    {
        var file = Read("cbr128.mp3");

        var padded = SoundFile.Read([.. new byte[215], .. file]);

        Assert.That(padded.AsSpan().SequenceEqual(SoundFile.Read(file).AsSpan()), Is.True);
    }

    // One channel, then two, as ISO's he_mode: the stream is of two, the first heard in both; the tag between is
    // passed by.
    [Test]
    public void FramesOfOneChannelAndOfTwo_AreAStreamOfTwo()
    {
        var mono = SoundFile.Read(Read("mono44.mp3"));

        var mixed = SoundFile.Read([.. Read("mono44.mp3"), .. Read("cbr128.mp3")]);

        Assert.That(mixed.Format, Is.EqualTo(new AudioFormat(44100, 2)));
        Assert.That(mixed.Frames, Is.GreaterThan(mono.Frames + 20000));
        var heard = mixed.AsSpan();
        for (var frame = 0; frame < mono.Frames; frame++)
        {
            Assert.That(heard[frame * 2], Is.EqualTo(mono.AsSpan()[frame]), $"frame {frame}");
            Assert.That(heard[frame * 2 + 1], Is.EqualTo(mono.AsSpan()[frame]), $"frame {frame}");
        }
    }

    [Test]
    public void WhatIsNoMp3_IsRefused()
    {
        byte[][] files =
        [
            [0xFF, 0xFB, 0x90],
            [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 10, .. new byte[10]],
            [.. "ID3"u8, 4, 0, 0, 0, 0, 0, 10, .. new byte[10], .. Enumerable.Repeat((byte)0x55, 5000)],
        ];

        foreach (var file in files)
        {
            Assert.That(() => SoundFile.Read(file), Throws.InstanceOf<InvalidDataException>());
        }
    }

    // Any byte of a file spoilt, or the file cut anywhere: refused, or read with silence for what is damaged.
    [Test]
    public void ASpoiltFile_IsRefusedOrRead()
    {
        var file = Read("lowest8.mp3");
        var random = new Random(2);
        for (var round = 0; round < 400; round++)
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

    [Test]
    public void Decoding_AllocatesNothing()
    {
        using var decoder = SoundFile.Open(new MemoryStream(Read("vbr48.mp3")));
        var samples = new float[4096 * 2];
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
        using var resource = typeof(Mp3Tests).Assembly.GetManifestResourceStream("mp3." + name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
