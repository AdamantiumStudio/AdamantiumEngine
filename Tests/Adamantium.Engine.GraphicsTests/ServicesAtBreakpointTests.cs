using System;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>At a breakpoint, the engine's services bring the world up to date as the game left it - called here from
/// inside a service's own update, as the drawing does while the world's thread is held there. Held in a game service's
/// update, each of the engine's does it, one that fails leaving the others to it, and no game service; held anywhere
/// else, none does.</summary>
[TestFixture]
public class ServicesAtBreakpointTests
{
    private AdamantiumDependencyContainer container;
    private Universe universe;
    private EntityServiceManager services;

    [SetUp]
    public void StartUniverse()
    {
        container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new HeadlessPlatform();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        universe.InitializeUniverse();
        services = universe.EntityWorld.ServiceManager;
    }

    [TearDown]
    public void ShutDown()
    {
        universe.ShutDown();
        GpuFixture.ReleaseRenderDevices();
    }

    [Test]
    public void HeldInAGameServicesUpdate_EachOfTheEngines_BringsItUpToDate_OneFailingLeavingTheOthers()
    {
        var world = universe.EntityWorld;
        var failing = world.CreateService<FailingService>(world);
        var game = world.CreateService<HeldGameService>(world);
        var placing = world.CreateService<PlacingService>(world);
        services.OnFrameEnded();
        game.IsActive = true;

        services.Update(default);

        Assert.Multiple(() =>
        {
            Assert.That(failing.Asked, Is.EqualTo(1), "asked, and failed");
            Assert.That(placing.Placed, Is.EqualTo(1), "brought up to date all the same");
            Assert.That(game.Placed, Is.EqualTo(0), "a game service's code is not run where no breakpoint stops it");
        });
    }

    [Test]
    public void HeldInsideAServiceOfTheEngines_NoneBringsItUpToDate()
    {
        var world = universe.EntityWorld;
        var placing = world.CreateService<PlacingService>(world);
        world.CreateService<HeldEngineService>(world);
        services.OnFrameEnded();

        services.Update(default);

        Assert.That(placing.Placed, Is.EqualTo(0), "the world half brought up to date by the engine is not touched");
    }

    [Test]
    public void HeldOutsideTheServicesUpdates_NoneBringsItUpToDate()
    {
        var world = universe.EntityWorld;
        var placing = world.CreateService<PlacingService>(world);
        services.OnFrameEnded();

        services.UpdateAtBreakpoint();

        Assert.That(placing.Placed, Is.EqualTo(0), "the engine may be halfway through changing the world");
    }

    private class PlacingService(EntityWorld world) : EntityService(world)
    {
        public int Placed { get; private set; }

        public override bool IsUpdateService => true;

        public override bool IsRenderingService => false;

        public override EntityServiceType ServiceType => EntityServiceType.Update;

        public override void UpdateAtBreakpoint() => Placed++;
    }

    private sealed class FailingService(EntityWorld world) : PlacingService(world)
    {
        public int Asked { get; private set; }

        public override void UpdateAtBreakpoint()
        {
            Asked++;
            throw new InvalidOperationException("Fails to bring it up to date.");
        }
    }

    // Its update is where the game is held: the drawing brings the world up to date meanwhile.
    private sealed class HeldGameService(EntityWorld world) : GameService(world)
    {
        public int Placed { get; private set; }

        public override void Update(AppTime appTime) => EntityWorld.ServiceManager.UpdateAtBreakpoint();

        public override void UpdateAtBreakpoint() => Placed++;
    }

    private sealed class HeldEngineService(EntityWorld world) : PlacingService(world)
    {
        public override void Update(AppTime appTime) => EntityWorld.ServiceManager.UpdateAtBreakpoint();
    }
}
