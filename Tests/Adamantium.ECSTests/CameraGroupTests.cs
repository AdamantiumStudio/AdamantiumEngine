using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>The world keeps its cameras in a group of their own, wherever they hang, so nothing walks its trees for
/// them.</summary>
[TestFixture]
public class CameraGroupTests
{
    [Test]
    public void ACameraDeepInATree_JoinsWithTheTree_AndLeavesWithIt()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var model = new Entity(null, "Model");
        var cockpit = new Entity(new Entity(model, "Body"), "Cockpit");
        cockpit.AddComponent(new Camera(60, 800, 600, 0.1f, 1000f));

        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        var joined = world.EntityManager.Cameras.Contains(cockpit);
        world.EntityManager.RemoveEntity(model);
        world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(joined, Is.True);
            Assert.That(world.EntityManager.Cameras.Count, Is.Zero);
        });
    }

    [Test]
    public void ACameraInAPartTakenOut_LeavesWithThePart()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var model = new Entity(null, "Model");
        var body = new Entity(model, "Body");
        var cockpit = new Entity(body, "Cockpit");
        cockpit.AddComponent(new Camera(60, 800, 600, 0.1f, 1000f));
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();

        world.EntityManager.RemoveEntity(body);
        world.ForceUpdate();

        Assert.That(world.EntityManager.Cameras.Count, Is.Zero);
    }
}
