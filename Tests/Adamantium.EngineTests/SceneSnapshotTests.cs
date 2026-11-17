using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A scene's state taken and put back - what Play in the editor does: what changed since goes back, the same
/// objects; what joined goes, unless kept; only what changed is put back.</summary>
[TestFixture]
public class SceneSnapshotTests
{
    private TestUniverse _universe;
    private EntityWorld _world;
    private Scene _scene;
    private Entity _group;
    private Entity _body;
    private Entity _lamp;

    [SetUp]
    public async Task MakeScene()
    {
        _universe = new TestUniverse();
        _universe.Satellites.Add<IUniverse>(_universe);
        _world = _universe.EntityWorld;
        SceneCode.Replace(_world, [], [typeof(SceneSnapshotTests).Assembly]);
        _scene = _world.MainScene;
        var file = new SceneFile();
        file.Markers.Add(new SceneMarker { Uid = 1, Name = "Group", Kind = SceneMarkerKind.Group, Position = new Vector3<double>(1, 0, 0) });
        file.Markers.Add(new SceneMarker { Uid = 2, Name = "Body", Parent = 1, Position = new Vector3<double>(0, 2, 0) });
        file.Lights.Add(new SceneLight { Uid = 3, Name = "Lamp", Type = LightType.Point, Intensity = 3, Range = 10 });
        var pushed = new SceneGameComponent { On = 2, Type = typeof(Pushed).FullName };
        pushed.Values.Add(new SceneValue(nameof(Pushed.Speed), "2"));
        file.GameComponents.Add(pushed);
        await new SceneBuilder(_scene, new CountingAssets()).Build(file);
        _world.ForceUpdate();
        _group = Named("Group");
        _body = Named("Body");
        _lamp = Named("Lamp");
    }

    [Test]
    public void WhatChanged_GoesBack()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        _group.Transform.Position = new Vector3<double>(9, 9, 9);
        _body.GetComponent<Pushed>().Speed = 7;
        _lamp.GetComponent<Light>().Intensity = 0.5f;
        _body.IsEnabled = false;
        _world.EntityManager.SetOwner(_body, null);
        _world.ForceUpdate();

        snapshot.Restore();
        _world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(_group.Transform.Position, Is.EqualTo(new Vector3<double>(1, 0, 0)));
            Assert.That(_body.GetComponent<Pushed>().Speed, Is.EqualTo(2));
            Assert.That(_lamp.GetComponent<Light>().Intensity, Is.EqualTo(3));
            Assert.That(_body.IsEnabled, Is.True);
            Assert.That(_body.Owner, Is.SameAs(_group));
            Assert.That(_body.Transform.Position, Is.EqualTo(new Vector3<double>(0, 2, 0)));
        });
    }

    [Test]
    public void WhatJoined_Goes_AndWhatWasTakenOut_ComesBack_TheSameObject()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        var spawned = new Entity(null, "Spawned");
        _world.EntityManager.AddEntity(spawned, _scene);
        var child = new Entity(_group, "Child");
        _world.EntityManager.RemoveEntity(_lamp);
        _world.ForceUpdate();

        snapshot.Restore();
        _world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.RootEntities, Does.Not.Contain(spawned));
            Assert.That(_group.Dependencies, Does.Not.Contain(child));
            Assert.That(_scene.RootEntities, Does.Contain(_lamp));
            Assert.That(_lamp.GetComponent<Light>().Intensity, Is.EqualTo(3));
        });
    }

    // A part taken out - not a root - comes back under its owner, in the world again.
    [Test]
    public void APartTakenOut_ComesBackUnderItsOwner()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        _world.EntityManager.RemoveEntity(_body);
        _world.ForceUpdate();

        snapshot.Restore();
        _world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(_body.Owner, Is.SameAs(_group));
            Assert.That(_group.Dependencies, Does.Contain(_body));
            Assert.That(_world.EntityManager.IsBeingRemoved(_body), Is.False);
            Assert.That(_body.Scene, Is.SameAs(_scene));
        });
    }

    // Hung under an entity of no scene, an entity leaves the scene: it comes back too.
    [Test]
    public void AnEntityHungOutsideTheScene_ComesBack()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        _world.EntityManager.SetOwner(_lamp, new Entity(null, "Nowhere"));
        _world.ForceUpdate();

        snapshot.Restore();
        _world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(_lamp.Scene, Is.SameAs(_scene));
            Assert.That(_scene.RootEntities, Does.Contain(_lamp));
        });
    }

    [Test]
    public void ANameAndAPlaceAmongTheRoots_GoBack()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        var order = _scene.RootEntities.ToList();
        _group.Name = "Renamed";
        _world.EntityManager.PlaceAt(_lamp, 0);
        _world.ForceUpdate();

        snapshot.Restore();
        _world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(_group.Name, Is.EqualTo("Group"));
            Assert.That(_scene.RootEntities, Is.EqualTo(order));
        });
    }

    [Test]
    public void AnEntityKept_Stays_WithWhatStandsInIt()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        var spawned = new Entity(null, "Spawned");
        _world.EntityManager.AddEntity(spawned, _scene);
        _world.ForceUpdate();
        var part = new Entity(spawned, "Part");

        snapshot.Restore(new HashSet<Entity> { spawned });
        _world.ForceUpdate();

        Assert.Multiple(() =>
        {
            Assert.That(_scene.RootEntities, Does.Contain(spawned));
            Assert.That(spawned.Dependencies, Does.Contain(part));
        });
    }

    // The cost of putting back follows the changes: only what changed is recorded.
    [Test]
    public void OnlyWhatChanged_IsRecorded()
    {
        SceneSnapshot.Take(_scene);

        _group.Transform.Position = new Vector3<double>(5, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Changes.Changed, Is.EquivalentTo(new[] { _group }));
            Assert.That(_scene.Changes.Added, Is.Empty);
            Assert.That(_scene.Changes.Removed, Is.Empty);
        });
    }

    [Test]
    public void AfterRestoring_NothingIsRecorded()
    {
        var snapshot = SceneSnapshot.Take(_scene);
        _group.Transform.Position = new Vector3<double>(5, 0, 0);

        snapshot.Restore();
        _group.Transform.Position = new Vector3<double>(6, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(_scene.Changes.IsRecording, Is.False);
            Assert.That(_scene.Changes.Changed, Is.Empty);
        });
    }

    private Entity Named(string name)
    {
        var stack = new Stack<Entity>(_scene.RootEntities);
        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            if (entity.Name == name)
                return entity;

            foreach (var part in entity.Dependencies)
            {
                stack.Push(part);
            }
        }

        Assert.Fail($"No {name} in the scene");
        return null;
    }
}
