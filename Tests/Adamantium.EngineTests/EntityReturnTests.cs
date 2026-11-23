using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>An entity taken out of the world and put back - a delete taken back in the editor: the same object comes
/// back, among its siblings where it stood; and a transform's state puts its entity back exactly, pivot and all.</summary>
[TestFixture]
public class EntityReturnTests
{
    [Test]
    public void ARootTakenOut_ComesBackTheSameObject_WhereItStood()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var first = new Entity(null, "First");
        var second = new Entity(null, "Second");
        var third = new Entity(null, "Third");
        manager.AddEntity(first);
        manager.AddEntity(second);
        manager.AddEntity(third);
        NextFrame(universe);
        var index = manager.GetSiblingIndex(second);
        manager.RemoveEntity(second);
        NextFrame(universe);

        manager.ReturnEntity(second, null, manager.MainScene, index);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(index, Is.EqualTo(1));
            Assert.That(manager.MainScene.RootEntities, Is.EqualTo(new[] { first, second, third }));
            Assert.That(manager.FindEntity(second.Uid), Is.SameAs(second));
        });
    }

    [Test]
    public void APartTakenOut_ComesBackUnderItsOwner_WhereItStood()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var first = new Entity(model, "First");
        var second = new Entity(model, "Second");
        var third = new Entity(model, "Third");
        manager.AddEntity(model);
        NextFrame(universe);
        var index = manager.GetSiblingIndex(second);
        manager.RemoveEntity(second);
        NextFrame(universe);
        var added = new List<Entity>();
        manager.EntityAdded += (_, e) => added.Add(e.Entity);

        manager.ReturnEntity(second, model, null, index);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(model.Dependencies, Is.EqualTo(new[] { first, second, third }));
            Assert.That(second.Owner, Is.SameAs(model));
            Assert.That(added, Is.EqualTo(new[] { second }));
        });
    }

    [Test]
    public void ARootHungUnderAnotherAndTakenOut_ComesBackUnderIt()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var lamp = new Entity(null, "Lamp");
        manager.AddEntity(model);
        manager.AddEntity(lamp);
        NextFrame(universe);
        manager.SetOwner(lamp, model);
        NextFrame(universe);
        var index = manager.GetSiblingIndex(lamp);
        manager.RemoveEntity(lamp);
        NextFrame(universe);

        manager.ReturnEntity(lamp, model, null, index);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(model.Dependencies, Is.EqualTo(new[] { lamp }));
            Assert.That(lamp.Owner, Is.SameAs(model));
            Assert.That(manager.MainScene.RootEntities, Is.EqualTo(new[] { model }));
        });
    }

    [Test]
    public void ARootHungUnderAnotherAndTakenOut_LeavesTheOthersProjection()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var model = new Entity(null, "Model");
        var lamp = new Entity(null, "Lamp");
        manager.AddEntity(model, projectionType: CameraProjectionType.Ortho);
        manager.AddEntity(lamp);
        NextFrame(universe);
        manager.SetOwner(lamp, model);
        NextFrame(universe);

        manager.RemoveEntity(lamp);
        NextFrame(universe);

        Assert.That(manager.GetCameraProjectionType(model), Is.EqualTo(CameraProjectionType.Ortho));
    }

    [Test]
    public void ARootHungUnderAnotherMovedToAnotherScene_LeavesThatScene_AsItIsTakenOut()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var other = manager.CreateScene("Other");
        var model = new Entity(null, "Model");
        var lamp = new Entity(null, "Lamp");
        lamp.AddComponent(new Light());
        manager.AddEntity(model);
        manager.AddEntity(lamp);
        NextFrame(universe);
        manager.SetOwner(lamp, model);
        NextFrame(universe);
        manager.MoveToScene(model, other);
        NextFrame(universe);
        var lightsAsItLeft = -1;
        manager.EntityRemoved += (_, _) => lightsAsItLeft = other.Components.CountOf<Light>();

        manager.RemoveEntity(lamp);
        NextFrame(universe);

        Assert.That(lightsAsItLeft, Is.Zero);
    }

    [Test]
    public void ARootHungOutsideTheWorldAndTakenOut_IsNotRecordedAsAdded()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var outside = new Entity(null, "Outside");
        var lamp = new Entity(null, "Lamp");
        manager.AddEntity(lamp);
        NextFrame(universe);
        manager.SetOwner(lamp, outside);
        NextFrame(universe);
        manager.MainScene.Changes.Start();

        manager.RemoveEntity(lamp);
        NextFrame(universe);
        manager.MainScene.Changes.Stop();

        Assert.That(manager.MainScene.Changes.Added, Does.Not.Contain(lamp));
    }

    [Test]
    public void AnEntity_IsPlacedAtAnIndex_AmongTheRootsAndUnderItsOwner()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var first = new Entity(null, "First");
        var second = new Entity(null, "Second");
        var model = new Entity(null, "Model");
        var part = new Entity(model, "Part");
        var mesh = new Entity(model, "Mesh");
        manager.AddEntity(first);
        manager.AddEntity(second);
        manager.AddEntity(model);
        NextFrame(universe);

        manager.PlaceAt(model, 0);
        manager.PlaceAt(mesh, 0);
        NextFrame(universe);

        Assert.Multiple(() =>
        {
            Assert.That(manager.MainScene.RootEntities, Is.EqualTo(new[] { model, first, second }));
            Assert.That(model.Dependencies, Is.EqualTo(new[] { mesh, part }));
        });
    }

    [Test]
    public void AnEntityInTheWorld_IsNotReturnedAgain()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var entity = new Entity(null, "Entity");
        manager.AddEntity(entity);
        NextFrame(universe);

        manager.ReturnEntity(entity, null, manager.MainScene, 0);
        NextFrame(universe);

        Assert.That(manager.MainScene.RootEntities, Is.EqualTo(new[] { entity }));
    }

    [Test]
    public void ATransformsState_PutsItsEntityBackExactly_PivotAndAll()
    {
        var entity = new Entity(null, "Entity");
        var transform = entity.Transform;
        transform.Rotation = Quaternion.CreateFromAxisAngle<float>(new Vector3F(0, 1, 0), 0.5f);
        transform.ScaleFactor = new Vector3F(2, 1, 1);
        transform.Pivot = new WorldPosition(1, 2, 3);
        var state = transform.State;
        var pivot = transform.Pivot;
        var position = transform.Position;

        transform.Pivot = new WorldPosition(-4, 0, 7);
        transform.Position = new WorldPosition(10, 10, 10);
        transform.Rotation = QuaternionF.Identity;
        transform.State = state;

        Assert.Multiple(() =>
        {
            Assert.That(transform.Position, Is.EqualTo(position));
            Assert.That(transform.Pivot, Is.EqualTo(pivot));
            Assert.That(transform.State, Is.EqualTo(state));
        });
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
