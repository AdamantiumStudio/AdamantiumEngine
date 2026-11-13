using System;
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
        manager.MainScene.RootEntities.CollectionChanged += (_, e) => changes.Add(e.Action);
        var entity = new Entity(null, "Entity");

        manager.AddEntity(entity);
        NextFrame(universe);
        manager.RemoveEntity(entity);
        NextFrame(universe);

        Assert.That(changes, Is.EqualTo(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove }));
        Assert.That(manager.MainScene.RootEntities, Is.Empty);
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

        Assert.That(manager.MainScene.RootEntities, Is.EqualTo(new[] { model }));
        Assert.That(model.Dependencies, Does.Contain(camera));
        Assert.That(manager.FindEntity(camera.Uid), Is.SameAs(camera), "still in the world");

        manager.RemoveOwner(camera);
        NextFrame(universe);

        Assert.That(manager.MainScene.RootEntities, Is.EquivalentTo(new[] { model, camera }));
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

        Assert.That(manager.MainScene.RootEntities, Is.Empty);
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
        Assert.That(manager.MainScene.RootEntities, Is.Empty);
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
        Assert.That(manager.MainScene.RootEntities, Is.EqualTo(new[] { model }));
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
        Assert.That(manager.MainScene.RootEntities, Is.Empty);
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

    // Added with no scene, an entity goes to the main one; added to a scene, to that one alone.
    [Test]
    public void TwoScenes_KeepTheirRootsApart()
    {
        var universe = new TestUniverse();
        var world = universe.EntityWorld;
        var arena = world.CreateScene("Arena");
        var ship = new Entity(null, "Ship");
        var rock = new Entity(null, "Rock");
        var mesh = new Entity(rock, "Mesh");

        world.EntityManager.AddEntity(ship);
        world.EntityManager.AddEntity(rock, arena);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(world.MainScene.RootEntities, Is.EqualTo(new[] { ship }));
            Assert.That(arena.RootEntities, Is.EqualTo(new[] { rock }));
            Assert.That(ship.Scene, Is.SameAs(world.MainScene));
            Assert.That(mesh.Scene, Is.SameAs(arena), "a branch is in its root's scene");
            Assert.That(mesh.World, Is.SameAs(world));
            Assert.That(world.Scenes, Is.EqualTo(new[] { world.MainScene, arena }));
        });
    }

    [Test]
    public void EachScene_HasAnIdOfItsOwn_ItIsFoundBy()
    {
        var world = new TestUniverse().EntityWorld;
        var arena = world.CreateScene("Arena");
        var hangar = world.CreateScene("Hangar");

        Assert.Multiple(() =>
        {
            Assert.That(new[] { world.MainScene.Uid, arena.Uid, hangar.Uid }, Is.Unique);
            Assert.That(world.EntityManager.FindScene(hangar.Uid), Is.SameAs(hangar));
            Assert.That(world.EntityManager.FindScene(UInt128.MaxValue), Is.Null);
        });
    }

    // A scene removed takes its trees out of the world - not another scene's - and takes nothing more in.
    [Test]
    public void ASceneRemoved_TakesItsTreesAlong_AndNoOtherScenes()
    {
        var universe = new TestUniverse();
        var world = universe.EntityWorld;
        var arena = world.CreateScene("Arena");
        var ship = new Entity(null, "Ship");
        var rock = new Entity(null, "Rock");
        var chase = new Entity(null, "Chase");
        world.EntityManager.AddEntity(ship);
        world.EntityManager.AddEntity(rock, arena);
        world.EntityManager.AddEntity(chase, arena);
        world.EntityManager.SetOwner(chase, rock);
        NextFrame(universe);

        world.RemoveScene(arena);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(arena.IsRemoved, Is.True);
            Assert.That(world.Scenes, Is.EqualTo(new[] { world.MainScene }));
            Assert.That(arena.RootEntities, Is.Empty);
            Assert.That(world.EntityManager.FindEntity(rock.Uid), Is.Null);
            Assert.That(world.EntityManager.FindEntity(chase.Uid), Is.Null);
            Assert.That(rock.Scene, Is.Null);
            Assert.That(world.MainScene.RootEntities, Is.EqualTo(new[] { ship }));
            Assert.That(() => world.EntityManager.AddEntity(new Entity(null, "Late"), arena),
                Throws.InvalidOperationException);
        });
    }

    // Added in the frame its scene is removed in, an entity never joins the world.
    [Test]
    public void AnEntityAddedToASceneRemovedBeforeTheFrame_NeverJoins()
    {
        var universe = new TestUniverse();
        var world = universe.EntityWorld;
        var arena = world.CreateScene("Arena");
        var rock = new Entity(null, "Rock");

        world.EntityManager.AddEntity(rock, arena);
        world.RemoveScene(arena);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(world.EntityManager.FindEntity(rock.Uid), Is.Null);
            Assert.That(rock.Scene, Is.Null);
            Assert.That(arena.RootEntities, Is.Empty);
        });
    }

    [Test]
    public void TheMainScene_IsNeverRemoved()
    {
        var world = new TestUniverse().EntityWorld;

        Assert.That(() => world.RemoveScene(world.MainScene), Throws.InvalidOperationException);
        Assert.That(world.Scenes, Is.EqualTo(new[] { world.MainScene }));
    }

    [Test]
    public void AnotherWorldsScene_TakesNoEntity()
    {
        var world = new TestUniverse().EntityWorld;
        var other = new TestUniverse().EntityWorld;

        Assert.That(() => world.EntityManager.AddEntity(new Entity(null, "Ship"), other.MainScene),
            Throws.ArgumentException);
    }

    // A camera following a model of another scene goes over to that scene; let go of, it is a root of that one.
    [Test]
    public void ARootGivenAnOwnerInAnotherScene_GoesOverToIt()
    {
        var universe = new TestUniverse();
        var world = universe.EntityWorld;
        var arena = world.CreateScene("Arena");
        var rock = new Entity(null, "Rock");
        var chase = new Entity(null, "Chase");
        world.EntityManager.AddEntity(rock, arena);
        world.EntityManager.AddEntity(chase);
        NextFrame(universe);

        world.EntityManager.SetOwner(chase, rock);
        NextFrame(universe);
        var held = chase.Scene;
        world.EntityManager.RemoveOwner(chase);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.SameAs(arena));
            Assert.That(world.MainScene.RootEntities, Is.Empty);
            Assert.That(arena.RootEntities, Is.EquivalentTo(new[] { rock, chase }));
            Assert.That(chase.Scene, Is.SameAs(arena));
        });
    }

    [Test]
    public void RemovingEverything_EmptiesEveryScene_AndKeepsTheScenes()
    {
        var universe = new TestUniverse();
        var world = universe.EntityWorld;
        var arena = world.CreateScene("Arena");
        world.EntityManager.AddEntity(new Entity(null, "Ship"));
        world.EntityManager.AddEntity(new Entity(null, "Rock"), arena);
        NextFrame(universe);

        world.RemoveAllEntities();
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(world.MainScene.RootEntities, Is.Empty);
            Assert.That(arena.RootEntities, Is.Empty);
            Assert.That(world.Scenes, Is.EqualTo(new[] { world.MainScene, arena }));
        });
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
