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

        _output = AudioOutput.CreateDefault(TimeSpan.FromMilliseconds(40));
        _output.Start(new RecordingRenderer());
        var problem = _output.DeviceProblem;
        _output.Stop();
        if (problem != null)
        {
            Assert.Ignore(problem);
        }
    }

    [TearDown]
    public void DisposeOutput() => _output?.Dispose();

    // Told by a device's own clock; without one it is ClockScheduleTests' to say, whose time a busy runner does not stop.
    private void OnlyOnADevice()
    {
        if (!toDevice)
        {
            Assert.Ignore("ClockScheduleTests has it");
        }
    }

    [Test]
    public void AsksForSoundAsFastAsItPlays()
    {
        OnlyOnADevice();
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
        OnlyOnADevice();
        _output.Start(new RecordingRenderer { Call = 30, AtCall = () => Thread.Sleep(300) });

        Assert.That(SpinWait.SpinUntil(() => _output.Statistics.Gaps > 0, Patience), Is.True);
        Assert.That(_output.Statistics.LongestRender, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(290)));
    }

    [Test]
    public void ARendererLateOnce_IsOneGap()
    {
        OnlyOnADevice();
        for (var round = 0; round < 5; round++)
        {
            var renderer = new RecordingRenderer { Call = 30, AtCall = () => Thread.Sleep(300) };
            _output.Start(renderer);
            Assert.That(SpinWait.SpinUntil(() => _output.Statistics.Gaps > 0, Patience), Is.True);
            var before = renderer.Frames;
            SpinWait.SpinUntil(() => renderer.Frames > before + _output.Format.SampleRate / 5, Patience);

            Assert.That(_output.Statistics.Gaps, Is.EqualTo(1), $"round {round}");
            _output.Stop();
        }
    }

    [Test]
    public void ARendererSlowerThanTheDevice_IsStoppedAllTheSame()
    {
        _output.Start(new SlowRenderer());
        Thread.Sleep(300);

        Assert.That(Task.Run(() => _output.Stop()).Wait(Patience), Is.True);
    }

    [TestCase(1, TestName = "StoppedFromItsRenderer_AtTheFirstCall_IsRefused")]
    [TestCase(5, TestName = "StoppedFromItsRenderer_IsRefused")]
    public void StoppedFromItsRenderer_IsRefused(int call)
    {
        Exception stopping = null;
        Exception disposing = null;
        var done = new ManualResetEventSlim();
        _output.Start(new RecordingRenderer
        {
            Call = call,
            AtCall = () =>
            {
                stopping = Catch(_output.Stop);
                disposing = Catch(_output.Dispose);
                done.Set();
            },
        });

        Assert.That(done.Wait(Patience), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(stopping, Is.InstanceOf<InvalidOperationException>());
            Assert.That(disposing, Is.InstanceOf<InvalidOperationException>());
            Assert.That(_output.Stop, Throws.Nothing, "it plays on, and is stopped from outside");
        });
    }

    private static Exception Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private sealed class SlowRenderer : IAudioRenderer
    {
        public void Render(Span<float> samples, AudioFormat format) => Thread.Sleep(60);
    }

    [Test]
    public void ARendererThatThrows_FailsTheOutput_AndStoppingSaysWhy()
    {
        _output.Start(new RecordingRenderer { Call = 3, AtCall = () => throw new InvalidOperationException("broken") });

        Assert.That(SpinWait.SpinUntil(() => _output.State == AudioOutputState.Failed, Patience), Is.True);
        Assert.That(() => _output.Stop(), Throws.InvalidOperationException.With.Message.EqualTo("broken"));
        Assert.That(() => _output.Start(new RecordingRenderer()), Throws.Nothing, "it can be started again");
    }

    // However soon it fails: the output was open, so starting it succeeded, and stopping it says why.
    [Test]
    public void ARendererThatThrowsAtOnce_FailsTheOutput_NotItsStart()
    {
        if (toDevice)
        {
            Assert.Ignore("a device is given its first sound before it opens");
        }

        for (var round = 0; round < 300; round++)
        {
            _output.Start(new RecordingRenderer { Call = 1, AtCall = () => throw new InvalidOperationException("broken") });

            Assert.That(() => _output.Stop(), Throws.InvalidOperationException.With.Message.EqualTo("broken"));
        }
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
