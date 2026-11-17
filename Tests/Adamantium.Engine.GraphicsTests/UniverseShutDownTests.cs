using System;
using System.Diagnostics;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>Shutting a universe down waits for its own loop a bounded time: a loop stuck in a frame does not hold the
/// caller - the application's exit - and finishes by itself once it is free.</summary>
[TestFixture]
public class UniverseShutDownTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void ALoopStuckInAFrame_DoesNotHoldTheShutDown_AndStopsByItselfOnceFree()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        var universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService())
        {
            LoopExitWait = TimeSpan.FromMilliseconds(200),
        };
        universe.InitializeUniverse();
        using var stuck = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        universe.Stopped += (_, _) => stopped.Set();
        universe.StartHostedLoop();
        try
        {
            universe.Post(() =>
            {
                entered.Set();
                stuck.Wait();
            });
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True, "the loop never ran the posted work");

            var clock = Stopwatch.StartNew();
            universe.ShutDown();
            var waited = clock.Elapsed;
            var stoppedWhileStuck = (stopped.IsSet, universe.IsStopped);
            stuck.Set();
            var stoppedOnceFree = stopped.Wait(TimeSpan.FromSeconds(10));
            for (var attempt = 0; attempt < 100 && !universe.IsStopped; attempt++)
            {
                Thread.Sleep(10);
            }

            Assert.Multiple(() =>
            {
                Assert.That(waited, Is.LessThan(TimeSpan.FromSeconds(3)), "the shut-down waited on the stuck loop");
                Assert.That(stoppedWhileStuck, Is.EqualTo((false, false)), "said stopped while stuck");
                Assert.That(stoppedOnceFree, Is.True, "the loop did not stop by itself once free");
                Assert.That(universe.IsStopped, Is.True);
            });
        }
        finally
        {
            stuck.Set();
            universe.ShutDown();
        }
    }
}
