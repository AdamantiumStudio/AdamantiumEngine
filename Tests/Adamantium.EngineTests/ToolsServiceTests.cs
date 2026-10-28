using System.Linq;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Tools;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class ToolsServiceTests
{
    [Test]
    public void ATool_TakesEffectWithTheNextFrame_AndReplacesTheOneInUse()
    {
        var tools = new ToolsService(new EntityWorld(new AdamantiumDependencyContainer(), new Satellites()));
        var move = new MoveTool();
        var rotate = new RotationTool();

        tools.Tool = move;
        Assert.That(tools.Processors, Is.Empty);

        tools.Update(new AppTime());
        Assert.That(tools.Processors, Is.EqualTo(new[] { move }));

        tools.Tool = rotate;
        tools.Update(new AppTime());
        Assert.That(tools.Processors, Is.EqualTo(new[] { rotate }));

        tools.Tool = null;
        tools.Update(new AppTime());
        Assert.That(tools.Processors, Is.Empty);
    }

    [Test]
    public void AToolChanged_WakesTheUniverse_WhileItsWatcherWatchesTheService()
    {
        var universe = new TestUniverse();
        var watcher = new UniverseWatcher(Wake) { IsWatching = true };
        universe.Satellites.Add(new Selection());
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        universe.Satellites.Add<IUniverseWatcher>(watcher);
        var tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();

        tools.Tool = new MoveTool();
        var watched = wakes;
        watcher.Woken();
        tools.UnloadContent();
        tools.Tool = new RotationTool();

        Assert.Multiple(() =>
        {
            Assert.That(watched, Is.EqualTo(1));
            Assert.That(wakes, Is.EqualTo(1), "a service taken out is no longer watched");
        });
    }

    private int wakes;

    private void Wake()
    {
        wakes++;
    }

    [Test]
    public void Hovered_IsForgottenEveryFrame_UntilTheToolFindsItAgain()
    {
        var tools = new ToolsService(new EntityWorld(new AdamantiumDependencyContainer(), new Satellites()));
        tools.Hovered = new Entity(null, "Under the pointer");

        tools.Update(new AppTime());

        Assert.That(tools.Hovered, Is.Null);
    }

    [Test]
    public void Selection_MarksWhatItHolds_AndOnlyThat()
    {
        var selection = new Selection();
        var first = new Entity(null, "First");
        var second = new Entity(null, "Second");

        selection.Current = first;
        Assert.That(first.IsSelected, Is.True);

        selection.Current = second;
        Assert.That(first.IsSelected, Is.False);
        Assert.That(second.IsSelected, Is.True);

        selection.Current = null;
        Assert.That(second.IsSelected, Is.False);
    }
}
