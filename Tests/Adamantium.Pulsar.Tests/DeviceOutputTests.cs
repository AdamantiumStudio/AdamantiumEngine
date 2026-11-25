using System.Diagnostics;
using Adamantium.Pulsar.Outputs;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>An output whose device comes and goes: it plays on by the clock meanwhile.</summary>
[TestFixture]
public class DeviceOutputTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Wanted = TimeSpan.FromMilliseconds(40);

    private FakeOutput _output;

    [SetUp]
    public void CreateOutput() => _output = new FakeOutput();

    [TearDown]
    public void DisposeOutput() => _output.Dispose();

    [Test]
    public void WithoutADevice_ItRunsSilent_AndSaysWhy()
    {
        _output.HasDevice = false;
        var renderer = new RecordingRenderer();

        _output.Start(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(_output.State, Is.EqualTo(AudioOutputState.Silent));
            Assert.That(_output.DeviceProblem, Is.EqualTo("gone"));
            Assert.That(SpinWait.SpinUntil(() => renderer.Frames > 4800, Patience), Is.True, "its time goes on");
        });
    }

    [Test]
    public void ADeviceComing_ItIsHeard()
    {
        _output.HasDevice = false;
        _output.Start(new RecordingRenderer());

        _output.HasDevice = true;

        Assert.That(SpinWait.SpinUntil(() => _output.State == AudioOutputState.Running, Patience), Is.True);
        Assert.That(_output.DeviceProblem, Is.Null);
    }

    [Test]
    public void ADeviceGone_ItPlaysOnSilent_AndClosesIt()
    {
        _output.Start(new RecordingRenderer());

        _output.HasDevice = false;

        Assert.That(SpinWait.SpinUntil(() => _output.State == AudioOutputState.Silent, Patience), Is.True);
        Assert.That(_output.Closed, Is.EqualTo(_output.Played));
    }

    [Test]
    public void AnotherDevice_ItIsOpenedAnew()
    {
        _output.Start(new RecordingRenderer());

        _output.Change = true;

        Assert.That(SpinWait.SpinUntil(() => _output.Played > 1, Patience), Is.True);
        Assert.That(_output.State, Is.EqualTo(AudioOutputState.Running));
    }

    [Test]
    public void TryingAgain_DoesNotSpeedUpTheTime()
    {
        _output.HasDevice = false;
        var renderer = new RecordingRenderer();
        var started = Stopwatch.GetTimestamp();

        _output.Start(renderer);

        Assert.That(SpinWait.SpinUntil(() => _output.Played > 3, Patience), Is.True);
        var ahead = renderer.Frames - AudioFormat.Default.FramesIn(Stopwatch.GetElapsedTime(started));
        Assert.That(ahead, Is.LessThanOrEqualTo(AudioFormat.Default.FramesIn(Wanted)), "a buffer ahead, not one for each try");
    }

    [Test]
    public void ADevicePlayedAWhileThenGone_AnotherIsOpenedAtOnce()
    {
        _output.Start(new RecordingRenderer());
        Thread.Sleep(1100);
        var states = _output.Played;

        _output.Lost = true;

        Assert.That(SpinWait.SpinUntil(() => _output.Played > states, Patience), Is.True);
        Assert.That(_output.TimeToReopen, Is.LessThan(TimeSpan.FromMilliseconds(900)));
        Assert.That(_output.State, Is.EqualTo(AudioOutputState.Running));
    }

    [Test]
    public void TheTimeBetweenTwoDevices_IsNoTimeBetweenFills()
    {
        _output.Start(new RecordingRenderer());
        Assert.That(SpinWait.SpinUntil(() => _output.Statistics.LongestBetweenFills > TimeSpan.Zero, Patience), Is.True);

        _output.OpensSlowly = true;
        _output.Change = true;

        Assert.That(SpinWait.SpinUntil(() => _output.Played > 1, Patience), Is.True);
        Assert.That(_output.Statistics.LongestBetweenFills, Is.LessThan(TimeSpan.FromMilliseconds(900)));
    }

    [Test]
    public void StoppedWhileSilent_ItStops()
    {
        _output.HasDevice = false;
        _output.Start(new RecordingRenderer());

        _output.Stop();

        Assert.That(_output.State, Is.EqualTo(AudioOutputState.Stopped));
    }

    [Test]
    public void ARendererFailingWhileSilent_FailsTheOutput()
    {
        _output.HasDevice = false;
        _output.Start(new RecordingRenderer { Call = 3, AtCall = () => throw new InvalidOperationException("broken") });

        Assert.That(SpinWait.SpinUntil(() => _output.State == AudioOutputState.Failed, Patience), Is.True);
        Assert.That(_output.Stop, Throws.InvalidOperationException);
    }

    private sealed class FakeOutput() : DeviceOutput(AudioFormat.Default, DeviceOutputTests.Wanted)
    {
        public volatile bool HasDevice = true;
        public volatile bool Change;
        public volatile bool Lost;
        public volatile bool OpensSlowly;
        private long _lostAt;
        private int _played;

        public TimeSpan TimeToReopen { get; private set; }

        private int _closed;

        public int Played => Volatile.Read(ref _played);

        public int Closed => Volatile.Read(ref _closed);

        protected override void PlayToDevice()
        {
            if (_lostAt != 0)
            {
                TimeToReopen = Stopwatch.GetElapsedTime(_lostAt);
            }

            if (!HasDevice)
            {
                Interlocked.Increment(ref _played);
                throw new DeviceException("gone");
            }

            if (OpensSlowly)
            {
                Thread.Sleep(1000);
                Filled(false);
            }

            PlaysToDevice(1920);
            Interlocked.Increment(ref _played);
            while (!Stopping.WaitOne(1) && !Change)
            {
                Filled(false);
                if (!HasDevice || Lost)
                {
                    Lost = false;
                    _lostAt = Stopwatch.GetTimestamp();
                    throw new DeviceException("gone");
                }
            }

            Change = false;
        }

        protected override void CloseDevice() => Interlocked.Increment(ref _closed);
    }
}
