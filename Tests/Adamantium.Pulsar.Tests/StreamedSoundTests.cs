using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>A sound read while it plays is heard as the one in memory is.</summary>
[TestFixture]
public class StreamedSoundTests
{
    private const int Block = 256;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private AudioEngine _engine;

    [SetUp]
    public void CreateEngine() => _engine = new AudioEngine();

    [TearDown]
    public void DisposeEngine() => _engine.Dispose();

    [TestCase(1f, false, 0)]
    [TestCase(1f, true, 0)]
    [TestCase(1.37f, false, 0)]
    [TestCase(0.5f, true, 0)]
    [TestCase(2.9f, true, 0)]
    [TestCase(1f, false, 50000)]
    [TestCase(1f, true, 100000)]
    public void Streamed_ItIsHeardAsInMemory(float pitch, bool loops, int from)
    {
        var settings = new VoiceSettings
        {
            Pitch = pitch, Loops = loops, From = TimeSpan.FromSeconds(from / 48000.0), Pan = -0.3f,
        };
        using var memory = new AudioEngine();
        var expected = memory.Play(memory.AddSound(SoundFile.Read(FlacFiles.Read("long16"))), settings);
        var voice = _engine.Play(_engine.AddSound(Streamed("long16")), settings);
        WaitUntilHeard();
        var first = true;

        for (var block = 0; block < 1500 && _engine.IsPlaying(voice); block++)
        {
            var heard = first ? _firstBlock : Render(_engine);
            first = false;
            Assert.That(heard, Is.EqualTo(Render(memory)).Within(1e-6f), $"block {block}");
            Assert.That(_engine.PositionOf(voice), Is.EqualTo(memory.PositionOf(expected)), $"block {block}");
            // As slow as an output takes it, or the reading is left behind.
            WaitUntilRead(voice);
        }

        Assert.That(_engine.StreamGaps, Is.Zero);
        Assert.That(_engine.IsPlaying(voice), Is.EqualTo(loops));
        Assert.That(memory.IsPlaying(expected), Is.EqualTo(loops));
    }

    [Test]
    public void AStereoWav_IsStreamedToo()
    {
        var samples = new FlacSignal(2, 16, 30000).ToFloats();
        _engine.Play(_engine.AddSound(StreamedSound.Open(new SoundMemoryStorage(WavFiles.Floats(48000, 2, samples)))));
        WaitUntilHeard();

        Assert.That(_firstBlock, Is.EqualTo(samples[..(Block * 2)]).Within(1e-6f));
    }

    [Test]
    public void VoicesOfOneSound_ReadItEachForItself()
    {
        var sound = _engine.AddSound(Streamed("long16"));
        _engine.Play(sound, new VoiceSettings { Pan = -1 });
        WaitUntilHeard();
        Render(_engine);
        _engine.Play(sound, new VoiceSettings { Pan = 1 });
        for (var block = 0; block < 2000; block++)
        {
            var heard = Render(_engine);
            if (heard[1] != 0 || heard[^1] != 0)
            {
                Assert.That(heard[^1], Is.EqualTo(FlacFiles.Long[Block - 1]).Within(1e-6f), "from its beginning");
                return;
            }

            Thread.Sleep(1);
        }

        Assert.Fail("the second voice was not heard");
    }

    [Test]
    public void Unheard_ItGoesOn()
    {
        var voice = _engine.Play(_engine.AddSound(Streamed("long16")), new VoiceSettings { Volume = 0 });

        Assert.That(
            SpinWait.SpinUntil(() =>
            {
                Render(_engine);
                WaitUntilRead(voice);
                return !_engine.IsPlaying(voice);
            }, Patience),
            Is.True);
    }

    [Test]
    public void Stopped_ItsFileIsClosed()
    {
        var storage = new CountingStorage(FlacFiles.Read("long16"));
        var sound = _engine.AddSound(StreamedSound.Open(storage));
        Assert.That(storage.Left, Is.Zero, "what it says was read");
        for (var round = 0; round < 3; round++)
        {
            var voice = _engine.Play(sound);
            Assert.That(SpinWait.SpinUntil(() => storage.Left == 1, Patience), Is.True);
            _engine.Stop(voice);

            Assert.That(
                SpinWait.SpinUntil(() =>
                {
                    Render(_engine);
                    return storage.Left == 0;
                }, Patience),
                Is.True, $"round {round}");
        }
    }

    [Test]
    public void TheEngineDisposed_FilesAreClosed_AndWhatThereIsPlaysOut()
    {
        var storage = new CountingStorage(FlacFiles.Read("long16"));
        var sound = _engine.AddSound(StreamedSound.Open(storage));
        var voice = _engine.Play(sound);
        WaitUntilHeard();

        _engine.Dispose();

        Assert.That(storage.Left, Is.Zero);
        for (var block = 0; block < 1000; block++)
        {
            Render(_engine);
        }

        Assert.That(_engine.IsPlaying(voice), Is.False);
        Assert.That(() => _engine.Play(sound), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public void AFileGone_EndsItsVoice_AndSaysWhy()
    {
        var storage = new CountingStorage(FlacFiles.Read("long16"));
        var sound = _engine.AddSound(StreamedSound.Open(storage));
        storage.IsGone = true;
        var voice = _engine.Play(sound);

        Assert.That(
            SpinWait.SpinUntil(() =>
            {
                Render(_engine);
                return !_engine.IsPlaying(voice);
            }, Patience),
            Is.True);
        Assert.That(_engine.StreamProblem, Is.EqualTo("gone"));
    }

    [Test]
    public void AFileDamagedLater_PlaysUntilThere()
    {
        var file = FlacFiles.Read("long16");
        file[file.Length / 2] ^= 1;
        var voice = _engine.Play(_engine.AddSound(StreamedSound.Open(new SoundMemoryStorage(file))));
        WaitUntilHeard();
        Assert.That(_firstBlock[0], Is.EqualTo(FlacFiles.Long[0] * 0.70710677f).Within(1e-6f));

        Assert.That(
            SpinWait.SpinUntil(() =>
            {
                Render(_engine);
                WaitUntilRead(voice);
                return !_engine.IsPlaying(voice);
            }, Patience),
            Is.True);
        Assert.That(_engine.StreamProblem, Does.Contain("damaged"));
    }

    [Test]
    public void ReadTooSlowly_ItIsSilent_AndGoesOnFromThere()
    {
        var storage = new CountingStorage(FlacFiles.Read("long16"));
        var voice = _engine.Play(_engine.AddSound(StreamedSound.Open(storage)), new VoiceSettings { Pan = -1 });
        WaitUntilHeard();
        storage.Block();
        var all = new List<float>(_firstBlock);
        for (var block = 0; block < 600; block++)
        {
            all.AddRange(Render(_engine));
        }

        Assert.That(_engine.StreamGaps, Is.EqualTo(1));
        Assert.That(all[^2], Is.Zero);
        Assert.That(_engine.IsPlaying(voice), Is.True);
        var position = _engine.PositionOf(voice);
        storage.Unblock();

        Assert.That(
            SpinWait.SpinUntil(() =>
            {
                Render(_engine);
                return _engine.PositionOf(voice) > position;
            }, Patience),
            Is.True);
    }

    [Test]
    public void ASoundThatDoesNotSayItsLength_IsNotStreamed()
    {
        var file = FlacFiles.Read("mono8");
        Array.Clear(file, 8 + 14, 4);
        file[8 + 13] &= 0xF0;

        Assert.That(() => StreamedSound.Open(new SoundMemoryStorage(file)), Throws.InstanceOf<InvalidDataException>());
        Assert.That(SoundFile.Read(file).Frames, Is.EqualTo(3000), "but is read whole");
    }

    [Test]
    public void RenderingStreams_AllocatesNothing()
    {
        var sound = _engine.AddSound(Streamed("long16"));
        for (var voice = 0; voice < 4; voice++)
        {
            _engine.Play(sound, new VoiceSettings { Loops = true, Pitch = 1 + voice * 0.1f });
        }

        WaitUntilHeard();
        var samples = new float[Block * 2];
        var least = long.MaxValue;
        for (var round = 0; round < 5; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var block = 0; block < 100; block++)
            {
                _engine.Render(samples, AudioFormat.Default);
            }

            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.That(least, Is.Zero);
    }

    private float[] _firstBlock;

    private static StreamedSound Streamed(string name) => StreamedSound.Open(new SoundMemoryStorage(FlacFiles.Read(name)));

    private static float[] Render(AudioEngine engine)
    {
        var samples = new float[Block * 2];
        engine.Render(samples, AudioFormat.Default);
        return samples;
    }

    // A voice begins once its first part was read.
    private void WaitUntilHeard()
    {
        Assert.That(
            SpinWait.SpinUntil(() =>
            {
                _firstBlock = Render(_engine);
                return _engine.AudibleVoices != 0;
            }, Patience),
            Is.True);
    }

    private void WaitUntilRead(VoiceId voice) =>
        Assert.That(SpinWait.SpinUntil(() => _engine.IsReadAhead || !_engine.IsPlaying(voice), Patience), Is.True);
}
