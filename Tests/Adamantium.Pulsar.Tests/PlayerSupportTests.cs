using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>What a player shows of a sound: its codec and tags, the frames just played, its waveform's peaks.</summary>
[TestFixture]
public class PlayerSupportTests
{
    // ffmpeg's: LIST INFO, Vorbis comments, OpusTags, ID3v2.3 (UTF-16) and v2.4 (UTF-8).
    [TestCase("tagged.wav", "WAV")]
    [TestCase("tagged.flac", "FLAC")]
    [TestCase("tagged.ogg", "Vorbis")]
    [TestCase("tagged.opus", "Opus")]
    [TestCase("tagged23.mp3", "MP3")]
    [TestCase("tagged24.mp3", "MP3")]
    public void TheTags_AreReadOfEachFormat(string name, string codec)
    {
        using var decoder = SoundFile.Open(new MemoryStream(Read(name)));

        Assert.That(decoder.Codec, Is.EqualTo(codec));
        Assert.That(decoder.Tags, Is.EqualTo(new SoundTags("Утро в лесу", "Pulsar Tests", "Звуки")));
    }

    [Test]
    public void AStreamedSound_SaysItsCodecAndTags()
    {
        var sound = StreamedSound.Open(new SoundMemoryStorage(Read("tagged.opus")));

        Assert.That(sound.Codec, Is.EqualTo("Opus"));
        Assert.That(sound.Tags.Title, Is.EqualTo("Утро в лесу"));
    }

    // An ID3v1 tag only, at the end.
    [Test]
    public void AnId3v1Tag_IsReadWhereThereIsNoOther()
    {
        var file = Read("tagged24.mp3");
        var start = 10 + (file[6] << 21 | file[7] << 14 | file[8] << 7 | file[9]);
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        "Rain"u8.CopyTo(tag.AsSpan(3));
        "Someone"u8.CopyTo(tag.AsSpan(33));

        using var decoder = SoundFile.Open(new MemoryStream([.. file.AsSpan(start), .. tag]));

        Assert.That(decoder.Tags, Is.EqualTo(new SoundTags("Rain", "Someone", null)));
    }

    [Test]
    public void ASoundOfNoTags_SaysNone()
    {
        using var decoder = SoundFile.Open(new MemoryStream(WavFiles.Floats(48000, 1, new float[100])));

        Assert.That(decoder.Tags.IsEmpty, Is.True);
    }

    // The last frames mixed, the newest last, across the end of the ring too.
    [Test]
    public void ThePlayedFrames_AreTheLastMixed()
    {
        using var engine = new AudioEngine();
        var samples = new float[2 * 6000];
        for (var frame = 0; frame < 6000; frame++)
        {
            samples[2 * frame] = samples[2 * frame + 1] = frame / 6000f;
        }

        engine.Play(engine.AddSound(new SoundData(AudioFormat.Default, samples)));
        var block = new float[2 * 1000];
        for (var round = 0; round < 5; round++)
        {
            engine.Render(block, AudioFormat.Default);
        }

        var played = new float[2 * 300];
        Assert.That(engine.CopyPlayed(played), Is.EqualTo(300));
        Assert.That(played[^2], Is.EqualTo(block[^2]));
        Assert.That(played[0], Is.EqualTo(block[2 * 700]));
        Assert.That(engine.CopyPlayed(new float[2 * 10000]), Is.EqualTo(AudioEngine.PlayedCapacity));
    }

    [Test]
    public void ThePeaks_AreTheLeastAndGreatestOfEachPart()
    {
        var samples = new float[2 * 1000];
        for (var frame = 0; frame < 1000; frame++)
        {
            samples[2 * frame] = MathF.Sin(frame * .05f);
            samples[2 * frame + 1] = frame / 1000f;
        }

        using var decoder = SoundFile.Open(new MemoryStream(WavFiles.Floats(48000, 2, samples)));
        var peaks = SoundPeaks.Of(decoder, 10);
        while (peaks.ReadOn(decoder, 300))
        {
            Assert.That(peaks.BucketsRead, Is.LessThan(10));
        }

        Assert.That(peaks.IsComplete, Is.True);
        for (var bucket = 0; bucket < 10; bucket++)
        {
            var part = Enumerable.Range(bucket * 100, 100);
            Assert.That(peaks.MinimumOf(0, bucket), Is.EqualTo(part.Min(frame => samples[2 * frame])));
            Assert.That(peaks.MaximumOf(0, bucket), Is.EqualTo(part.Max(frame => samples[2 * frame])));
            Assert.That(peaks.MaximumOf(1, bucket), Is.EqualTo((bucket * 100 + 99) / 1000f));
        }
    }

    private static byte[] Read(string name)
    {
        using var resource = typeof(PlayerSupportTests).Assembly.GetManifestResourceStream("tags." + name);
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
