using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A scene keeps its cameras in a group of their own, wherever they hang, so nothing walks its trees for
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
        cockpit.AddComponent(NewCamera());

        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        var joined = world.MainScene.Cameras.Contains(cockpit);
        world.EntityManager.RemoveEntity(model);
        world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(joined, Is.True);
            Assert.That(world.MainScene.Cameras.Count, Is.Zero);
        });
    }

    [Test]
    public void ACameraInAPartTakenOut_LeavesWithThePart()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var model = new Entity(null, "Model");
        var body = new Entity(model, "Body");
        var cockpit = new Entity(body, "Cockpit");
        cockpit.AddComponent(NewCamera());
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();

        world.EntityManager.RemoveEntity(body);
        world.ForceUpdate();

        Assert.That(world.MainScene.Cameras.Count, Is.Zero);
    }

    [Test]
    public void ACamera_JoinsTheCamerasOfItsOwnSceneOnly()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var arena = world.CreateScene("Arena");
        var view = new Entity(null, "View");
        view.AddComponent(NewCamera());

        world.EntityManager.AddEntity(view, arena);
        world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(arena.Cameras.Contains(view), Is.True);
            Assert.That(world.MainScene.Cameras.Count, Is.Zero);
        });
    }

    [Test]
    public void ACameraGivenAnOwnerInAnotherScene_MovesToThatScenesCameras()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var arena = world.CreateScene("Arena");
        var model = new Entity(null, "Model");
        var chase = new Entity(null, "Chase");
        chase.AddComponent(NewCamera());
        world.EntityManager.AddEntity(model, arena);
        world.EntityManager.AddEntity(chase);
        world.ForceUpdate();

        world.EntityManager.SetOwner(chase, model);
        world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(chase.Scene, Is.SameAs(arena));
            Assert.That(arena.Cameras.Contains(chase), Is.True);
            Assert.That(world.MainScene.Cameras.Contains(chase), Is.False);
            Assert.That(world.MainScene.RootEntities, Does.Not.Contain(chase));
        });
    }

    private static Camera NewCamera() => new(60, 800, 600, 0.1f, 1000f);
}
