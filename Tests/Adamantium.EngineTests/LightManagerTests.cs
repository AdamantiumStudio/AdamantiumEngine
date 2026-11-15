using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Managers;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Models;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class LightManagerTests
{
    [Test]
    public void AnAddedLight_IsListedFromTheNextFrame_ByItsKind()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld.MainScene);
        var spot = new LightTemplate().BuildEntity(null, "Spot", LightType.Spot);

        lights.AddLight(spot);

        Assert.That(lights.SpotLights, Is.Empty, "the frame already running keeps its lists");

        NextFrame(universe);

        Assert.That(lights.SpotLights, Is.EqualTo(new[] { spot.GetComponent<Light>() }));
        Assert.That(lights.PointLights, Is.Empty);
        Assert.That(lights.DirectionalLights, Is.Empty);
        Assert.That(universe.EntityWorld.MainScene.RootEntities, Does.Contain(spot));
    }

    [Test]
    public void ARemovedLight_LeavesTheListsAndTheWorld()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld.MainScene);
        var point = new LightTemplate().BuildEntity(null, "Point", LightType.Point);
        lights.AddLight(point);
        NextFrame(universe);
        Assert.That(universe.EntityWorld.MainScene.RootEntities, Does.Contain(point));

        lights.RemoveLight(point);
        NextFrame(universe);

        Assert.That(lights.Lights, Is.Empty);
        Assert.That(universe.EntityWorld.MainScene.RootEntities, Does.Not.Contain(point));
    }

    // The frame already running keeps its entities, as it keeps its lights: a light deleted from the application's thread
    // left the world at once, while the render thread was going through it, and the frame failed.
    [Test]
    public void ARemovedLight_StaysInTheWorldUntilTheFrameEnds()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld.MainScene);
        var point = new LightTemplate().BuildEntity(null, "Point", LightType.Point);
        lights.AddLight(point);
        NextFrame(universe);

        lights.RemoveLight(point);

        Assert.That(universe.EntityWorld.MainScene.RootEntities, Does.Contain(point));
    }

    [Test]
    public void AnEntityWithoutALight_IsNotTakenIn()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld.MainScene);

        lights.AddLight(new Entity(null, "Not a light"));
        NextFrame(universe);

        Assert.That(lights.Lights, Is.Empty);
        Assert.That(universe.EntityWorld.MainScene.RootEntities, Is.Empty);
    }

    // The lights follow the world: a light inside a model comes with the model and goes with it, with no call of its own.
    [Test]
    public void ALightInsideAModel_IsListedWithIt_AndLeavesWithIt()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var lights = new LightManager(universe, manager.MainScene);
        var model = new Entity(null, "Model");
        var lamp = new LightTemplate().BuildEntity(model, "Lamp", LightType.Point);

        manager.AddEntity(model);
        NextFrame(universe);
        var listed = lights.PointLights.Count;
        manager.RemoveEntity(model);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(listed, Is.EqualTo(1));
            Assert.That(lights.Lights, Is.Empty);
            Assert.That(lights.Contains(lamp), Is.False);
        });
    }

    // An undo puts a light back through the world, not through the lights: it is listed again all the same.
    [Test]
    public void ALightTakenOutAndReturned_IsListedAgain()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var lights = new LightManager(universe, manager.MainScene);
        var spot = new LightTemplate().BuildEntity(null, "Spot", LightType.Spot);
        manager.AddEntity(spot);
        NextFrame(universe);
        manager.RemoveEntity(spot);
        NextFrame(universe);

        manager.ReturnEntity(spot, null, manager.MainScene, 0);
        NextFrame(universe);

        Assert.That(lights.SpotLights, Is.EqualTo(new[] { spot.GetComponent<Light>() }));
    }

    [Test]
    public void TheLightsAlreadyInTheScene_AreListed()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var sun = new LightTemplate().BuildEntity(null, "Sun", LightType.Directional);
        manager.AddEntity(sun);
        NextFrame(universe);

        var lights = new LightManager(universe, manager.MainScene);
        NextFrame(universe);

        Assert.That(lights.DirectionalLights, Is.EqualTo(new[] { sun.GetComponent<Light>() }));
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
