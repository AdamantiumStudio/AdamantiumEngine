using Adamantium.Core.DependencyInjection;
using Adamantium.Graphics.Core;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Outputs taken out of a universe: freed, the main one handed on - and nothing else touched. A paused frame
/// does only this, so a pause and a resume with no output taken out leave every output as it was.</summary>
[TestFixture]
public class UniversePlatformTests
{
    [Test]
    public void AFrameWithNothingTakenOut_LeavesEveryOutputAsItWas()
    {
        var (platform, first, second) = TwoOutputs();

        platform.RemoveWindowsInternal();

        Assert.Multiple(() =>
        {
            Assert.That(platform.Outputs, Is.EqualTo((UniverseOutput[])[first, second]));
            Assert.That(platform.MainWindow, Is.SameAs(first));
            Assert.That(first.IsDisposed || second.IsDisposed, Is.False);
        });
    }

    [Test]
    public void TakingTheMainOutputOut_FreesIt_AndHandsMainToTheNext()
    {
        var (platform, first, second) = TwoOutputs();

        platform.RemoveOutput(first.OutputContext);
        platform.RemoveWindowsInternal();

        Assert.Multiple(() =>
        {
            Assert.That(platform.Outputs, Is.EqualTo((UniverseOutput[])[second]));
            Assert.That(platform.MainWindow, Is.SameAs(second));
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.False);
        });
    }

    [Test]
    public void TakingTheLastOutputOut_LeavesNoMain()
    {
        var (platform, first, second) = TwoOutputs();

        platform.RemoveOutput(first.OutputContext);
        platform.RemoveOutput(second.OutputContext);
        platform.RemoveWindowsInternal();

        Assert.Multiple(() =>
        {
            Assert.That(platform.Outputs, Is.Empty);
            Assert.That(platform.MainWindow, Is.Null);
        });
    }

    private static (UniversePlatform Platform, TestOutput First, TestOutput Second) TwoOutputs()
    {
        var universe = new TestUniverse();
        var registry = (IContainerRegistry)universe.Container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        registry.RegisterInstance<IGamepadBackend>(new NoGamepadBackend());
        universe.Satellites.Add<IUniverseEventAggregator>(new UniverseEventAggregator());
        universe.Satellites.Add<IGraphicsDeviceService>(new NoDeviceGraphicsService());

        var platform = new UniversePlatform(universe);
        universe.RaiseInitialized();

        var first = new TestOutput { Width = 0, Height = 0 };
        var second = new TestOutput { Width = 0, Height = 0 };
        platform.AddOutput(first);
        platform.AddOutput(second);
        platform.MakePreparationsForNextFrame();
        return (platform, first, second);
    }
}
