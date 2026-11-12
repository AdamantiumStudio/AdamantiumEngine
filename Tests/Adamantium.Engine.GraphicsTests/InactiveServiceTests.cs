using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A service that is not active is passed by in the frame - not updated, drawn or presented - and told the
/// frame ended; made active again, it takes part again.</summary>
[TestFixture]
public class InactiveServiceTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AnInactiveService_IsNotUpdatedDrawnOrPresented_AndIsAgainWhenActive()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        var universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        universe.InitializeUniverse();
        var world = universe.EntityWorld;
        var service = world.CreateService<CountingService>(world);
        var services = world.ServiceManager;
        services.OnFrameEnded();
        service.Reset();

        service.IsActive = false;
        services.Update(default);
        services.Draw(default);
        services.Present();
        var asleep = (service.Updates, service.Draws, service.Presents, service.FramesEnded, services.HasActiveRenderingService);

        service.IsActive = true;
        services.Update(default);
        services.Draw(default);
        services.Present();

        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.EqualTo((0, 0, 0, 1, false)), "asleep: only told the frame ended");
            Assert.That((service.Updates, service.Draws, service.Presents), Is.EqualTo((1, 1, 1)));
            Assert.That(services.HasActiveRenderingService, Is.True);
        });
        universe.ShutDown();
    }

    private sealed class CountingService : EntityService
    {
        public CountingService(EntityWorld world) : base(world)
        {
        }

        public int Updates { get; private set; }

        public int Draws { get; private set; }

        public int Presents { get; private set; }

        public int FramesEnded { get; private set; }

        public override bool IsUpdateService => true;

        public override bool IsRenderingService => true;

        public override EntityServiceType ServiceType => EntityServiceType.Render;

        public override void Update(AppTime appTime) => Updates++;

        public override bool BeginDraw() => true;

        public override void Draw(AppTime appTime) => Draws++;

        public override void Present() => Presents++;

        public override void FrameEnded() => FramesEnded++;

        public void Reset() => (Updates, Draws, Presents, FramesEnded) = (0, 0, 0, 0);
    }
}
