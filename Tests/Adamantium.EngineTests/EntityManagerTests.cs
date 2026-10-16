using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.ECS;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class EntityManagerTests
{
    // A hierarchy view binds to the roots: an entity added to the world and one taken out are both announced.
    [Test]
    public void TheRoots_AnnounceWhatComesAndGoes()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var changes = new List<NotifyCollectionChangedAction>();
        manager.RootEntities.CollectionChanged += (_, e) => changes.Add(e.Action);
        var entity = new Entity(null, "Entity");

        manager.AddEntity(entity);
        NextFrame(universe);
        manager.RemoveEntity(entity);
        NextFrame(universe);

        Assert.That(changes, Is.EqualTo(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove }));
        Assert.That(manager.RootEntities, Is.Empty);
    }

    // A camera following a model is given the model as its owner: it is the model's child then, and listed under it, not
    // also among the roots - a hierarchy showed it twice. Let go of, it is a root again; in the world all along.
    [Test]
    public void AnEntityGivenAnOwner_LeavesTheRoots_AndComesBackWhenLetGo()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var camera = new Entity(null, "Camera");
        manager.AddEntity(model);
        manager.AddEntity(camera);
        NextFrame(universe);

        manager.SetOwner(camera, model);
        NextFrame(universe);

        Assert.That(manager.RootEntities, Is.EqualTo(new[] { model }));
        Assert.That(model.Dependencies, Does.Contain(camera));
        Assert.That(manager.FindEntity(camera.Uid), Is.SameAs(camera), "still in the world");

        manager.SetOwner(camera, null);
        NextFrame(universe);

        Assert.That(manager.RootEntities, Is.EquivalentTo(new[] { model, camera }));
    }

    // The world is a tree: the model taken out of it takes its branches along, the camera following it among them.
    [Test]
    public void AnEntityWhoseOwnerLeavesTheWorld_LeavesWithIt()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var camera = new Entity(null, "Camera");
        manager.AddEntity(model);
        manager.AddEntity(camera);
        manager.SetOwner(camera, model);
        NextFrame(universe);

        manager.RemoveEntity(model);
        NextFrame(universe);

        Assert.That(manager.RootEntities, Is.Empty);
        Assert.That(manager.FindEntity(camera.Uid), Is.Null);
    }

    [Test]
    public void RemovingEverything_TakesTheOwnedOnesToo()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var camera = new Entity(null, "Camera");
        manager.AddEntity(model);
        manager.AddEntity(camera);
        manager.SetOwner(camera, model);
        NextFrame(universe);

        manager.RemoveAllEntities();
        NextFrame(universe);

        Assert.That(manager.FindEntity(camera.Uid), Is.Null);
        Assert.That(manager.RootEntities, Is.Empty);
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
