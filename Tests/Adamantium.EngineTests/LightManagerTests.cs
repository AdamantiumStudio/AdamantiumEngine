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
        var lights = new LightManager(universe, universe.EntityWorld);
        var spot = new LightTemplate().BuildEntity(null, "Spot", LightType.Spot);

        lights.AddLight(spot);

        Assert.That(lights.SpotLights, Is.Empty, "the frame already running keeps its lists");

        NextFrame(universe);

        Assert.That(lights.SpotLights, Is.EqualTo(new[] { spot.GetComponent<Light>() }));
        Assert.That(lights.PointLights, Is.Empty);
        Assert.That(lights.DirectionalLights, Is.Empty);
        Assert.That(universe.EntityWorld.RootEntities, Does.Contain(spot));
    }

    [Test]
    public void ARemovedLight_LeavesTheListsAndTheWorld()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld);
        var point = new LightTemplate().BuildEntity(null, "Point", LightType.Point);
        lights.AddLight(point);
        NextFrame(universe);
        Assert.That(universe.EntityWorld.RootEntities, Does.Contain(point));

        lights.RemoveLight(point);
        NextFrame(universe);

        Assert.That(lights.Lights, Is.Empty);
        Assert.That(universe.EntityWorld.RootEntities, Does.Not.Contain(point));
    }

    // The frame already running keeps its entities, as it keeps its lights: a light deleted from the application's thread
    // left the world at once, while the render thread was going through it, and the frame failed.
    [Test]
    public void ARemovedLight_StaysInTheWorldUntilTheFrameEnds()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld);
        var point = new LightTemplate().BuildEntity(null, "Point", LightType.Point);
        lights.AddLight(point);
        NextFrame(universe);

        lights.RemoveLight(point);

        Assert.That(universe.EntityWorld.RootEntities, Does.Contain(point));
    }

    [Test]
    public void AnEntityWithoutALight_IsNotTakenIn()
    {
        var universe = new TestUniverse();
        var lights = new LightManager(universe, universe.EntityWorld);

        lights.AddLight(new Entity(null, "Not a light"));
        NextFrame(universe);

        Assert.That(lights.Lights, Is.Empty);
        Assert.That(universe.EntityWorld.RootEntities, Is.Empty);
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
