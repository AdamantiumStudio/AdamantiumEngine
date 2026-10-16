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

        camera.Owner = model;
        NextFrame(universe);

        Assert.That(manager.RootEntities, Is.EqualTo(new[] { model }));
        Assert.That(model.Dependencies, Does.Contain(camera));
        Assert.That(manager.FindEntity(camera.Uid), Is.SameAs(camera), "still in the world");

        camera.Owner = null;
        NextFrame(universe);

        Assert.That(manager.RootEntities, Is.EquivalentTo(new[] { model, camera }));
    }

    // The model taken out of the world takes nothing of the camera's walk with it: held by nothing of the world any more,
    // the camera is a root again, or no transform, drawing or picking would reach it.
    [Test]
    public void AnEntityWhoseOwnerLeavesTheWorld_IsARootAgain()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var camera = new Entity(null, "Camera");
        manager.AddEntity(model);
        manager.AddEntity(camera);
        camera.Owner = model;
        NextFrame(universe);

        manager.RemoveEntity(model);
        NextFrame(universe);

        Assert.That(manager.RootEntities, Is.EqualTo(new[] { camera }));
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
        camera.Owner = model;
        NextFrame(universe);

        manager.RemoveAllEntities();
        NextFrame(universe);

        Assert.That(manager.FindEntity(camera.Uid), Is.Null);
        Assert.That(manager.RootEntities, Is.Empty);
    }

    // Whoever hears of a new owner - on another thread, a frame later - reads that owner, not the one it replaced.
    [Test]
    public void AnOwnerIsSetBeforeItIsAnnounced()
    {
        var model = new Entity(null, "Model");
        var camera = new Entity(null, "Camera");
        Entity heard = null;
        camera.OwnerChanged += (_, _) => heard = camera.Owner;

        camera.Owner = model;

        Assert.That(heard, Is.SameAs(model));
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
