using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>Ogg files of libvorbis and of ffmpeg's own Vorbis coder, against libvorbisfile's decoding of each, the
/// reference: kept as 16-bit FLAC, so within half a step of 16 bits, and what two decoders' rounding leaves. FLAC in Ogg
/// against the native file of the same samples.</summary>
[TestFixture]
public class OggTests
{
    private const float Within = 1.2f / 32768;

    [TestCase("stereo44", 44100, 2)]
    [TestCase("mono22", 22050, 1)]
    [TestCase("lowest8", 8000, 1)]
    [TestCase("best48", 48000, 2)]
    [TestCase("three32", 32000, 3)]
    [TestCase("surround48", 48000, 6)]
    [TestCase("eight48", 48000, 8)]
    // Of ffmpeg's coder: blocks of one size alone.
    [TestCase("ffmpeg48", 48000, 2)]
    // Pages of 50 ms.
    [TestCase("pages22", 22050, 1)]
    // Its granules 100000 on, as a stream cut from another's middle: read from its first sample all the same.
    [TestCase("later22", 22050, 1)]
    // Pages of a second: the last holds more than two long blocks.
    [TestCase("long8", 8000, 1)]
    public void WhatTheReferenceDecodes_IsDecoded(string name, int rate, int channels)
    {
        var sound = SoundFile.Read(Read(name + ".ogg"));
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

        Assert.That(most, Is.LessThanOrEqualTo(Within), $"frame {at / channels}, channel {at % channels}");
    }

    [TestCase("pages22.ogg")]
    [TestCase("trimmed22.ogg")]
    [TestCase("later22.ogg")]
    // Into its last page, of a second: decoding begins on a page before it.
    [TestCase("long8.ogg")]
    [TestCase("stereo44.ogg")]
    [TestCase("surround48.ogg")]
    [TestCase("flac22.oga")]
    public void Sought_ItReadsOnAsFromTheBeginning(string name)
    {
        var whole = SoundFile.Read(Read(name));
        var channels = whole.Format.Channels;
        using var decoder = SoundFile.Open(new MemoryStream(Read(name)));
        var random = new Random(1);
        var samples = new float[700 * channels];
        int[] frames = [0, 1, 127, 128, 1023, 1024, whole.Frames - 1, whole.Frames, 0, .. Enumerable.Range(0, 100).Select(_ => random.Next(whole.Frames))];
        foreach (var frame in frames)
        {
            decoder.Seek(frame);
            var got = decoder.Read(samples);

            Assert.That(got, Is.EqualTo(Math.Min(700, whole.Frames - frame)), $"frame {frame}");
            Assert.That(samples.AsSpan(0, got * channels).SequenceEqual(whole.AsSpan().Slice(frame * channels, got * channels)),
                Is.True, $"frame {frame}");
        }
    }

    // Its granules 300 back: its first page says fewer samples than its packets have, which are cut from the beginning
    // (A.2), and the last says the end 300 sooner. libvorbis cuts the end alone, of a first page that holds more than
    // the two packets A.2 asks for.
    [Test]
    public void AFirstPageOfFewerSamples_CutsThemFromTheBeginning()
    {
        var whole = SoundFile.Read(Read("pages22.ogg"));

        var trimmed = SoundFile.Read(Read("trimmed22.ogg"));

        Assert.That(trimmed.AsSpan().SequenceEqual(whole.AsSpan()[300..]), Is.True);
    }

    // One page of audio, its granule 100000 on: the stream begins there, not at 0, and has all its packets' samples,
    // 26 blocks of 1024 overlapped, as no granule cuts its end now.
    [Test]
    public void OnePageOfALaterStream_IsItsSamples()
    {
        var file = Read("mono22.ogg");
        var whole = SoundFile.Read(file);
        var last = OggPages.Of(file)[^1];
        OggPages.SetGranule(file, last, OggPages.GranuleOf(file, last) + 100000);
        OggPages.Seal(file, last);

        var later = SoundFile.Read(file);

        Assert.That(later.Frames, Is.EqualTo(26 * 512));
        Assert.That(later.AsSpan()[..whole.AsSpan().Length].SequenceEqual(whole.AsSpan()), Is.True);
    }

    // The last page's granule spoilt, its checksum no longer its own: the length is the page's before it, read whole.
    [Test]
    public void TheLastPagesGranuleSpoilt_IsNotTheLength()
    {
        var file = Read("long8.ogg");
        var pages = OggPages.Of(file);
        var before = OggPages.GranuleOf(file, pages[^2]);
        file[pages[^1] + 12] = 0x40;

        using var decoder = SoundFile.Open(new MemoryStream(file));

        Assert.That(decoder.Frames, Is.EqualTo(before));
        Assert.That(decoder.Read(new float[before + 1000]), Is.EqualTo(before));
    }

    // An Opus stream before Vorbis's: Vorbis is the sound, as with any stream beside it of a codec not read.
    [Test]
    public void AnOpusStreamBefore_IsPassedBy()
    {
        var file = Read("beside22.ogg");
        OggPages.SetFirstBytes(file, 0, "OpusHead"u8);
        OggPages.Seal(file, 0);

        var sound = SoundFile.Read(file);

        Assert.That(sound.AsSpan().SequenceEqual(SoundFile.Read(Read("pages22.ogg")).AsSpan()), Is.True);
    }

    [Test]
    public void Streamed_ItIsReadToo()
    {
        var sound = StreamedSound.Open(new SoundMemoryStorage(Read("pages22.ogg")));

        Assert.That(sound.Frames, Is.EqualTo(SoundFile.Read(Read("pages22.ogg")).Frames));
    }

    [Test]
    public void FlacInOgg_IsTheNativeFilesSamples()
    {
        var sound = SoundFile.Read(Read("flac22.oga"));

        Assert.That(sound.AsSpan().SequenceEqual(SoundFile.Read(Read("flac22.flac")).AsSpan()), Is.True);
    }

    // The checksum of its first page spoilt: FLAC all the same, which checks its frames' own.
    [Test]
    public void FlacInOgg_OfAFirstPageNotWhole_IsFlacStill()
    {
        var file = Read("flac22.oga");
        file[22] ^= 0xFF;

        var sound = SoundFile.Read(file);

        Assert.That(sound.AsSpan().SequenceEqual(SoundFile.Read(Read("flac22.flac")).AsSpan()), Is.True);
    }

    [Test]
    public void TheLoopOfItsComments_IsItsLoop()
    {
        Assert.That(SoundFile.Read(Read("looped22.ogg")).Loop, Is.EqualTo(new SoundLoop(1000, 21000)));
    }

    // FLAC of a codec renamed, its pages between Vorbis's: the stream of a codec known is read.
    [Test]
    public void AStreamBeside_OfACodecNotKnown_IsPassedBy()
    {
        var sound = SoundFile.Read(Read("beside22.ogg"));

        Assert.That(sound.AsSpan().SequenceEqual(SoundFile.Read(Read("pages22.ogg")).AsSpan()), Is.True);
    }

    // Of a link after the first: the first is the sound.
    [Test]
    public void AChainedFile_IsItsFirstLink()
    {
        var first = Read("pages22.ogg");

        var chained = SoundFile.Read([.. first, .. Read("stereo44.ogg")]);

        Assert.That(chained.AsSpan().SequenceEqual(SoundFile.Read(first).AsSpan()), Is.True);
    }

    [Test]
    public void Opus_IsRefusedAsNotReadYet()
    {
        Assert.That(() => SoundFile.Read(Read("opus22.ogg")),
            Throws.InstanceOf<InvalidDataException>().With.Message.Contains("Opus"));
    }

    // A page spoilt, its checksum not its own: what it held is silence, of the length the granules say; what is after
    // it, from the next packet that begins on a page whole, is as before.
    [Test]
    public void ADamagedPage_IsSilenceAndTheRestIsRead()
    {
        var file = Read("pages22.ogg");
        var whole = SoundFile.Read(file);
        var pages = PagesOf(file);
        var spoilt = (byte[])file.Clone();
        var page = pages[pages.Count / 2];
        spoilt[page + 40] ^= 0x55;

        var sound = SoundFile.Read(spoilt);

        Assert.That(sound.Frames, Is.EqualTo(whole.Frames));
        var heard = sound.AsSpan().ToArray();
        var expected = whole.AsSpan().ToArray();
        var differing = Enumerable.Range(0, sound.Frames).Where(frame => heard[frame] != expected[frame]).ToArray();
        Assert.That(differing, Is.Not.Empty);
        // A page of 50 ms, and the blocks overlapping on either side of it.
        Assert.That(differing[^1] - differing[0], Is.LessThan(22050 / 20 + 3 * 1024));
    }

    [Test]
    public void WhatIsNoOgg_IsRefused()
    {
        byte[][] files =
        [
            [.. "OggS"u8],
            [.. "OggS"u8, .. new byte[40]],
            [.. Read("pages22.ogg").AsSpan(0, 200)],
            [.. Read("pages22.ogg").AsSpan(0, 4000).ToArray().Select((value, index) => index > 100 ? (byte)0 : value)],
        ];

        foreach (var file in files)
        {
            Assert.That(() => SoundFile.Read(file), Throws.InstanceOf<InvalidDataException>());
        }
    }

    // Any byte of a file spoilt, or the file cut anywhere: refused, or read, nothing that is no number.
    [TestCase("pages22.ogg")]
    [TestCase("stereo44.ogg")]
    [TestCase("flac22.oga")]
    public void ASpoiltFile_IsRefusedOrRead(string name)
    {
        var file = Read(name);
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

    [TestCase("pages22.ogg")]
    [TestCase("surround48.ogg")]
    public void Decoding_AllocatesNothing(string name)
    {
        using var decoder = SoundFile.Open(new MemoryStream(Read(name)));
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

    private static List<int> PagesOf(byte[] file)
    {
        var pages = new List<int>();
        for (var at = 0; at + 27 <= file.Length;)
        {
            pages.Add(at);
            var segments = file[at + 26];
            at += 27 + segments + file.AsSpan(at + 27, segments).ToArray().Sum(length => length);
        }

        return pages;
    }

    private static byte[] Read(string name)
    {
        using var resource = typeof(OggTests).Assembly.GetManifestResourceStream("ogg." + name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
