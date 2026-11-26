using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>A player's commands, through an engine rendered here a block at a time.</summary>
[TestFixture]
public class AudioPlaybackTests
{
    private const int Rate = 48000;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private AudioEngine _engine;
    private AudioPlayback _playback;

    [SetUp]
    public void SetUp()
    {
        _engine = new AudioEngine();
        _playback = new AudioPlayback(_engine);
    }

    [TearDown]
    public void TearDown()
    {
        _playback.Dispose();
        _engine.Dispose();
    }

    [Test]
    public void AnOpenSound_SaysWhatItIs()
    {
        _playback.Open(Storage(PlayerSupportTests.Read("tagged.opus")));

        var info = _playback.Info;
        Assert.That(info.Codec, Is.EqualTo("Opus"));
        Assert.That(info.Format, Is.EqualTo(new AudioFormat(48000, 1)));
        Assert.That(info.Tags.Title, Is.EqualTo("Утро в лесу"));
        Assert.That(info.Duration.TotalMilliseconds, Is.EqualTo(100).Within(1));
        Assert.That(_playback.Duration, Is.EqualTo(info.Duration));
        // Of the file over its length: 24 kbit/s asked, and the Ogg pages and tags.
        Assert.That(info.Bitrate, Is.InRange(24000, 120000));
        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Stopped));
        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ASoundPlayedToTheEnd_Stops_AndSaysItEnded()
    {
        var ended = 0;
        var changes = new List<AudioPlaybackState>();
        _playback.Ended += (_, _) => ended++;
        _playback.StateChanged += (_, _) => changes.Add(_playback.State);
        _playback.Open(Storage(Sine(1000, 0.2)));

        _playback.Play();
        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Playing));
        RenderUntil(() => _playback.State == AudioPlaybackState.Stopped);

        Assert.That(ended, Is.EqualTo(1));
        Assert.That(changes, Is.EqualTo(new[] { AudioPlaybackState.Playing, AudioPlaybackState.Stopped }));
        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void APausedSound_StaysWhereItIs_AndGoesOnFromThere()
    {
        _playback.Open(Storage(Sine(1000, 2)));
        _playback.Play();
        RenderUntil(() => _playback.Position >= TimeSpan.FromMilliseconds(200));

        // As every command of the engine's, from the next block it mixes.
        _playback.Pause();
        Render(1);
        var paused = _playback.Position;
        Render(50);

        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Paused));
        Assert.That(_playback.Position, Is.EqualTo(paused));
        _playback.Play();
        RenderUntil(() => _playback.Position > paused);
    }

    [Test]
    public void ASeekWhileStopped_IsWhereItPlaysFrom()
    {
        _playback.Open(Storage(Sine(1000, 2)));

        _playback.Seek(TimeSpan.FromSeconds(1.5));
        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        _playback.Play();
        RenderUntil(() => _playback.Position > TimeSpan.FromSeconds(1.5));

        Assert.That(_playback.Position, Is.LessThan(TimeSpan.FromSeconds(1.7)));
    }

    [Test]
    public void ASeekWhilePlaying_GoesOnFromThere()
    {
        _playback.Open(Storage(Sine(1000, 2)));
        _playback.Play();
        RenderUntil(() => _playback.Position > TimeSpan.FromMilliseconds(100));

        _playback.Seek(TimeSpan.FromSeconds(1.5));
        Render(5);

        Assert.That(_playback.Position, Is.InRange(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.7)));
        Assert.That(_engine.Voices, Is.EqualTo(1));
    }

    // Seeking past the end ends it.
    [Test]
    public void ASeekPastTheEnd_IsKeptWithinTheSound()
    {
        _playback.Open(Storage(Sine(1000, 0.5)));

        _playback.Seek(TimeSpan.FromSeconds(10));
        Assert.That(_playback.Position, Is.EqualTo(_playback.Duration));
        _playback.Seek(TimeSpan.FromSeconds(-1));
        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void AStoppedSound_IsBackAtTheBeginning()
    {
        _playback.Open(Storage(Sine(1000, 2)));
        _playback.Play();
        RenderUntil(() => _playback.Position > TimeSpan.FromMilliseconds(100));

        _playback.Stop();
        Render(5);

        Assert.That(_playback.State, Is.EqualTo(AudioPlaybackState.Stopped));
        Assert.That(_playback.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(_engine.Voices, Is.Zero);
    }

    [Test]
    public void TheVolume_IsWhatIsHeard()
    {
        _playback.Open(Storage(Sine(1000, 2)));
        _playback.Volume = 0.25f;
        _playback.Play();
        RenderUntil(() => _playback.Position > TimeSpan.FromMilliseconds(100));

        var block = Render(1);

        Assert.That(block.Max(), Is.EqualTo(0.5f * 0.25f).Within(0.01f));
    }

    // A sine of half the full scale: -6 dB in its band, the farthest bands silent.
    [Test]
    public void TheSpectrum_IsOfWhatIsHeard()
    {
        _playback.Open(Storage(Sine(1000, 2)));
        var bands = new float[32];
        _playback.ReadSpectrum(bands);
        Assert.That(bands, Is.All.Zero);

        _playback.Play();
        RenderUntil(() => _playback.Position > TimeSpan.FromMilliseconds(200));
        _playback.ReadSpectrum(bands);

        var loudest = Array.IndexOf(bands, bands.Max());
        var low = 40 * Math.Pow(400, (double)loudest / bands.Length);
        var high = 40 * Math.Pow(400, (loudest + 1.0) / bands.Length);
        Assert.That(1000, Is.InRange(low - 24, high + 24));
        Assert.That(bands[loudest], Is.EqualTo(1 - 6.02f / 72).Within(0.02f));
        Assert.That(bands[0], Is.LessThan(0.2f));
        Assert.That(bands[^1], Is.LessThan(0.2f));
    }

    [Test]
    public void TheWaveform_IsReadInTheBackground()
    {
        _playback.Open(Storage(Sine(1000, 1)));

        var waveform = _playback.ReadWaveform(20, CancellationToken.None);

        Assert.That(SpinWait.SpinUntil(() => waveform.IsComplete, Patience), Is.True);
        Assert.That(waveform.MaximumOf(0, 10), Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(waveform.MinimumOf(1, 10), Is.EqualTo(-0.5f).Within(0.001f));
    }

    [Test]
    public void NoSoundFile_IsNotOpened()
    {
        _playback.Open(Storage(Sine(1000, 1)));

        Assert.Throws<InvalidDataException>(() => _playback.Open(Storage([1, 2, 3, 4, 5, 6, 7, 8])));
        Assert.That(_playback.Info, Is.Null);
        Assert.Throws<InvalidOperationException>(() => _playback.Play());
    }

    // Of its own engine, to an output of its own.
    [Test]
    public void APlaybackOfItsOwn_PlaysToItsOutput()
    {
        using var playback = new AudioPlayback(AudioOutput.CreateSilent(TimeSpan.FromMilliseconds(20)));
        var ended = new ManualResetEventSlim();
        playback.Ended += (_, _) => ended.Set();
        playback.Open(Storage(Sine(1000, 0.1)));

        playback.Play();

        Assert.That(ended.Wait(Patience), Is.True);
        Assert.That(playback.State, Is.EqualTo(AudioPlaybackState.Stopped));
    }

    private static SoundMemoryStorage Storage(byte[] file) => new(file);

    // Stereo, half of full scale.
    private static byte[] Sine(double frequency, double seconds)
    {
        var frames = (int)(Rate * seconds);
        var samples = new float[2 * frames];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[2 * frame] = samples[2 * frame + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * frame / Rate));
        }

        return WavFiles.Floats(Rate, 2, samples);
    }

    // Blocks of 10 ms, each once the streamed sound was read ahead; the last of them.
    private float[] Render(int blocks)
    {
        var block = new float[2 * 480];
        for (var count = 0; count < blocks; count++)
        {
            Assert.That(SpinWait.SpinUntil(() => _engine.IsReadAhead, Patience), Is.True);
            _engine.Render(block, AudioFormat.Default);
        }

        return block;
    }

    private void RenderUntil(Func<bool> condition)
    {
        for (var count = 0; count < 2000 && !condition(); count++)
        {
            Render(1);
        }

        Assert.That(condition(), Is.True);
    }
}
