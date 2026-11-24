using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A drawing of the world where it stands, in its first frame, has the service that draws an output added in
/// that frame put in at once; a service of the game's steps added with it waits for the frame's end, on the world's
/// thread, as ever.</summary>
[TestFixture]
public class RenderingServicesTakenInTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void OnlyTheRenderingServices_AreTakenIn_BeforeTheFramesEnd()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new HeadlessPlatform();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        var universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        try
        {
            universe.InitializeUniverse();
            var world = universe.EntityWorld;
            var services = world.ServiceManager;
            var drawing = world.CreateService<DrawingService>(world);
            var stepping = world.CreateService<SteppingService>(world);

            var drawnBefore = services.HasActiveRenderingService;
            services.TakeInRenderingServices();
            services.Draw(default);
            services.Update(default);

            Assert.Multiple(() =>
            {
                Assert.That(drawnBefore, Is.False, "added, not yet in");
                Assert.That(drawing.Draws, Is.EqualTo(1), "the rendering service is in, and draws");
                Assert.That(stepping.Updates, Is.EqualTo(0), "the game's step waits for the frame's end");
            });

            services.OnFrameEnded();
            services.Update(default);
            Assert.That(stepping.Updates, Is.EqualTo(1), "in once the frame ended");
        }
        finally
        {
            universe.ShutDown();
        }
    }

    private sealed class DrawingService : EntityService
    {
        public DrawingService(EntityWorld world) : base(world)
        {
        }

        public int Draws { get; private set; }

        public override bool IsUpdateService => false;

        public override bool IsRenderingService => true;

        public override EntityServiceType ServiceType => EntityServiceType.Render;

        public override bool BeginDraw() => true;

        public override void Draw(AppTime appTime) => Draws++;
    }

    private sealed class SteppingService : EntityService
    {
        public SteppingService(EntityWorld world) : base(world)
        {
        }

        public int Updates { get; private set; }

        public override bool IsUpdateService => true;

        public override bool IsRenderingService => false;

        public override EntityServiceType ServiceType => EntityServiceType.Update;

        public override void Update(AppTime appTime) => Updates++;
    }
}
