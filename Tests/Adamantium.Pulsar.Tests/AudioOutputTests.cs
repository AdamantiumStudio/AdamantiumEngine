using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>An output of either kind: the clock's, and the default device's where the machine has one - a CI runner has
/// none.</summary>
[TestFixture(false)]
[TestFixture(true)]
public class AudioOutputTests(bool toDevice)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private IAudioOutput _output;

    [SetUp]
    public void CreateOutput()
    {
        if (!toDevice)
        {
            _output = AudioOutput.CreateSilent(TimeSpan.FromMilliseconds(40));
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("no device output here yet");
        }

        _output = AudioOutput.CreateDefault(TimeSpan.FromMilliseconds(40));
        try
        {
            _output.Start(new RecordingRenderer());
            _output.Stop();
        }
        catch (AudioException e)
        {
            Assert.Ignore(e.Message);
        }
    }

    [TearDown]
    public void DisposeOutput() => _output?.Dispose();

    [Test]
    public void AsksForSoundAsFastAsItPlays()
    {
        var renderer = new RecordingRenderer();
        _output.Start(renderer);
        var buffer = _output.Format.FramesIn(_output.Buffer);
        Assert.That(SpinWait.SpinUntil(() => renderer.Frames >= buffer * 2, Patience), Is.True);
        var before = renderer.Frames;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        Thread.Sleep(1000);

        var expected = _output.Format.FramesIn(System.Diagnostics.Stopwatch.GetElapsedTime(started));
        Assert.That(renderer.Frames - before, Is.EqualTo(expected).Within(buffer * 2));
    }

    [Test]
    public void Started_ItRuns_AndSaysItsBuffer_AndStoppedAsksNoMore()
    {
        var renderer = new RecordingRenderer();

        _output.Start(renderer);
        var state = _output.State;
        var buffer = _output.Buffer;
        _output.Stop();
        var frames = renderer.Frames;
        Thread.Sleep(100);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(AudioOutputState.Running));
            Assert.That(buffer, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(39)));
            Assert.That(_output.State, Is.EqualTo(AudioOutputState.Stopped));
            Assert.That(frames, Is.Positive);
            Assert.That(renderer.Frames, Is.EqualTo(frames));
        });
    }

    [Test]
    public void StartedAgain_ItPlaysOn()
    {
        _output.Start(new RecordingRenderer());
        _output.Stop();
        var renderer = new RecordingRenderer();

        _output.Start(renderer);

        Assert.That(SpinWait.SpinUntil(() => renderer.Frames > _output.Format.SampleRate / 10, Patience), Is.True);
    }

    // What the measurement of the garbage collector's pauses counts by.
    [Test]
    public void ARendererLateByMoreThanTheBuffer_IsAGap()
    {
        _output.Start(new RecordingRenderer { Call = 5, AtCall = () => Thread.Sleep(300) });

        Assert.That(SpinWait.SpinUntil(() => _output.Statistics.Gaps > 0, Patience), Is.True);
        Assert.That(_output.Statistics.LongestRender, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(290)));
    }

    [Test]
    public void ARendererThatThrows_FailsTheOutput_AndStoppingSaysWhy()
    {
        _output.Start(new RecordingRenderer { Call = 3, AtCall = () => throw new InvalidOperationException("broken") });

        Assert.That(SpinWait.SpinUntil(() => _output.State == AudioOutputState.Failed, Patience), Is.True);
        Assert.That(() => _output.Stop(), Throws.InvalidOperationException.With.Message.EqualTo("broken"));
        Assert.That(() => _output.Start(new RecordingRenderer()), Throws.Nothing, "it can be started again");
    }

    [Test]
    public void Playing_ItsThreadAllocatesNothing()
    {
        var renderer = new RecordingRenderer();
        _output.Start(renderer);

        Assert.That(SpinWait.SpinUntil(() => renderer.Frames > _output.Format.SampleRate, Patience), Is.True);
        Assert.That(renderer.AllocatedBetweenCalls, Is.Zero);
    }

    [Test]
    public void StartedTwice_IsRefused()
    {
        _output.Start(new RecordingRenderer());

        Assert.That(() => _output.Start(new RecordingRenderer()), Throws.InvalidOperationException);
    }
}
