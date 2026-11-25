using Adamantium.Pulsar.Outputs;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>An output whose device comes and goes: it plays on by the clock meanwhile.</summary>
[TestFixture]
public class DeviceOutputTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

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

    private sealed class FakeOutput() : DeviceOutput(AudioFormat.Default, TimeSpan.FromMilliseconds(40))
    {
        public volatile bool HasDevice = true;
        public volatile bool Change;
        private int _played;
        private int _closed;

        public int Played => Volatile.Read(ref _played);

        public int Closed => Volatile.Read(ref _closed);

        protected override void PlayToDevice()
        {
            Interlocked.Increment(ref _played);
            if (!HasDevice)
            {
                throw new DeviceException("gone");
            }

            PlaysToDevice(1920);
            while (!Stopping.WaitOne(1) && !Change)
            {
                if (!HasDevice)
                {
                    throw new DeviceException("gone");
                }
            }

            Change = false;
        }

        protected override void CloseDevice() => Interlocked.Increment(ref _closed);
    }
}
