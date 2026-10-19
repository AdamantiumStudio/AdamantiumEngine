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

        manager.RemoveOwner(camera);
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

    // A mesh of a model is no entity of the world's own, only a branch of the model's tree: taken out, it leaves the
    // model, announced once however often asked in a frame, and the camera following it goes with it.
    [Test]
    public void APartOfAModel_IsTakenOutOfIt_WithWhatFollowsIt()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var part = new Entity(model, "Part");
        var camera = new Entity(null, "Camera");
        manager.AddEntity(model);
        manager.AddEntity(camera);
        manager.SetOwner(camera, part);
        NextFrame(universe);
        var removed = new List<Entity>();
        manager.EntityRemoved += (_, e) => removed.Add(e.Entity);

        manager.RemoveEntity(part);
        manager.RemoveEntity(part);
        NextFrame(universe);

        Assert.That(model.Dependencies, Does.Not.Contain(part));
        Assert.That(manager.RootEntities, Is.EqualTo(new[] { model }));
        Assert.That(manager.FindEntity(camera.Uid), Is.Null);
        Assert.That(removed, Is.EqualTo(new[] { part, camera }));
    }

    // Taken out, the part has no owner left: asked again it is outside the world, and it can be hung anywhere anew.
    [Test]
    public void APartTakenOut_IsAnnouncedOnce_AndCanBeHungAgain()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var part = new Entity(model, "Part");
        var mesh = new Entity(part, "Mesh");
        manager.AddEntity(model);
        NextFrame(universe);
        var removed = new List<Entity>();
        manager.EntityRemoved += (_, e) => removed.Add(e.Entity);

        manager.RemoveEntity(part);
        NextFrame(universe);
        manager.RemoveEntity(part);
        manager.RemoveEntity(mesh);
        NextFrame(universe);

        Assert.That(removed, Is.EqualTo(new[] { part }));
        Assert.That(part.Owner, Is.Null);
        Assert.That(part.Dependencies, Does.Contain(mesh));

        manager.SetOwner(part, model);
        NextFrame(universe);

        Assert.That(model.Dependencies, Does.Contain(part));
    }

    // Asked twice in a frame, or for a part and the model holding it, the world announces each entity once.
    [Test]
    public void AModelTakenOutTwice_OrWithItsPart_IsAnnouncedOnce()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var part = new Entity(model, "Part");
        manager.AddEntity(model);
        NextFrame(universe);
        var removed = new List<Entity>();
        manager.EntityRemoved += (_, e) => removed.Add(e.Entity);

        manager.RemoveEntity(part);
        manager.RemoveEntity(model);
        manager.RemoveEntity(model);
        NextFrame(universe);

        Assert.That(removed, Is.EqualTo(new[] { model }));
        Assert.That(manager.RootEntities, Is.Empty);
    }

    // The part stays in place until the frame: nothing walking the model's tree meanwhile sees it go.
    [Test]
    public void APartTakenOut_StaysUntilTheNextFrame()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var part = new Entity(model, "Part");
        manager.AddEntity(model);
        NextFrame(universe);

        manager.RemoveEntity(part);

        Assert.That(model.Dependencies, Does.Contain(part));
        NextFrame(universe);
        Assert.That(model.Dependencies, Does.Not.Contain(part));
    }

    [Test]
    public void AnEntityOutsideTheWorld_IsLeftAlone()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var owner = new Entity(null, "Owner");
        var part = new Entity(owner, "Part");
        var removed = new List<Entity>();
        manager.EntityRemoved += (_, e) => removed.Add(e.Entity);

        manager.RemoveEntity(part);
        NextFrame(universe);

        Assert.That(owner.Dependencies, Does.Contain(part));
        Assert.That(removed, Is.Empty);
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
