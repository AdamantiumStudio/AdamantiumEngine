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

    // A click on a model's part takes the model first - moved, it is the model that moves, and its handles stay on it -
    // and the parts once anything of the model is selected, a part next to the one selected too.
    [Test]
    public void AClickOnAPart_ChoosesTheModelFirst_ThenItsParts()
    {
        var selection = new Selection();
        var (model, body, glass, wheel) = Model();

        Assert.Multiple(() =>
        {
            Assert.That(selection.ChosenByClick(glass), Is.SameAs(model), "nothing selected: the model");
            Assert.That(selection.ChosenByClick(null), Is.Null);
        });

        selection.Current = model;
        Assert.That(selection.ChosenByClick(glass), Is.SameAs(glass), "the model selected: the part under the pointer");

        selection.Current = wheel;
        Assert.Multiple(() =>
        {
            Assert.That(selection.ChosenByClick(glass), Is.SameAs(glass), "another part selected: this one");
            Assert.That(selection.ChosenByClick(body), Is.SameAs(body));
        });

        selection.Current = new Entity(null, "Another") { Source = "Another.dae" };
        Assert.That(selection.ChosenByClick(glass), Is.SameAs(model), "another model selected: this one whole");
    }

    // Only a root made from a file is a model: a group the user made over models, or an entity made in the scene, is
    // taken as it is.
    [Test]
    public void OnlyARootMadeFromAFile_IsAModel()
    {
        var selection = new Selection();
        var group = new Entity(null, "Group");
        var (model, _, glass, _) = Model(group);
        var shape = new Entity(group, "Box");

        Assert.Multiple(() =>
        {
            Assert.That(Selection.ModelOf(glass), Is.SameAs(model));
            Assert.That(selection.ChosenByClick(glass), Is.SameAs(model), "the model, not the group over it");
            Assert.That(Selection.ModelOf(shape), Is.Null);
            Assert.That(selection.ChosenByClick(shape), Is.SameAs(shape), "made in the scene: itself");
        });
    }

    // Control takes out what a part belongs to as it is selected: the model, or the part itself.
    [Test]
    public void TakingOut_ChoosesTheSelectedOwnerOfThePart()
    {
        var selection = new Selection();
        var (model, body, glass, _) = Model();

        selection.Current = model;
        Assert.That(selection.ChosenToRemove(glass), Is.SameAs(model));

        selection.Current = body;
        Assert.That(selection.ChosenToRemove(glass), Is.SameAs(body));

        selection.Current = null;
        Assert.That(selection.ChosenToRemove(glass), Is.SameAs(model), "nothing of it selected: as a click would");
    }

    private static (Entity Model, Entity Body, Entity Glass, Entity Wheel) Model(Entity owner = null)
    {
        var model = new Entity(owner, "Car") { Source = "Car.dae" };
        var body = new Entity(model, "Body");
        return (model, body, new Entity(body, "Glass"), new Entity(model, "Wheel"));
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
