using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A frame's time counts the frame itself: its total is the sum of the frame times so far, paused or not.
/// </summary>
[TestFixture]
public class UniverseTimeTests
{
    private Universe _universe;
    private TimeRecordingService _service;

    [SetUp]
    public void MakeUniverse()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        _universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        _universe.InitializeUniverse();
        var world = _universe.EntityWorld;
        _service = world.CreateService<TimeRecordingService>(world);
        world.ServiceManager.OnFrameEnded();
    }

    [TearDown]
    public void ReleaseDevices()
    {
        _universe?.ShutDown();
        GpuFixture.ReleaseRenderDevices();
    }

    [Test]
    public void TotalTime_CountsTheFrameItIsGivenIn()
    {
        RunFrames(3);
        _universe.IsSimulationPaused = true;
        RunFrames(2);
        _universe.IsSimulationPaused = false;
        RunFrames(2);

        Assert.That(_service.Frames, Has.Count.EqualTo(7));
        var total = 0.0;
        for (var frame = 0; frame < _service.Frames.Count; frame++)
        {
            var time = _service.Frames[frame];
            total += time.FrameTime;
            Assert.That(time.TotalTime.TotalSeconds, Is.EqualTo(total).Within(1e-6), $"frame {frame}");
            if (frame is 3 or 4)
                Assert.That(time.FrameTime, Is.Zero, $"paused frame {frame}");
            else if (frame > 0)
                Assert.That(time.FrameTime, Is.GreaterThan(0.01), $"frame {frame}");
        }
    }

    [Test]
    public void PausePosted_HoldsTheFrameItIsTakenIn()
    {
        RunFrames(2);

        _universe.Post(() => _universe.IsSimulationPaused = true);
        RunFrames(1);

        Assert.That(_service.Frames[2].FrameTime, Is.Zero);
        Assert.That(_service.Frames[2].TotalTime, Is.EqualTo(_service.Frames[1].TotalTime));
    }

    [Test]
    public void UniversePausedByAPostedAction_PassesNoTimeInThatFrame()
    {
        RunFrames(2);

        _universe.Post(_universe.Pause);
        RunFrames(1);

        Assert.That(_service.Frames, Has.Count.EqualTo(3));
        Assert.That(_service.Frames[2].FrameTime, Is.Zero);
        Assert.That(_service.Frames[2].TotalTime, Is.EqualTo(_service.Frames[1].TotalTime));
    }

    [Test]
    public void LongGap_CountsAQuarterSecondAtMost()
    {
        RunFrames(1);

        Thread.Sleep(400);
        _universe.RunOnce(default);

        Assert.That(_service.Frames[1].FrameTime, Is.EqualTo(0.25));
    }

    private void RunFrames(int count)
    {
        for (var frame = 0; frame < count; frame++)
        {
            Thread.Sleep(20);
            _universe.RunOnce(default);
        }
    }
}
