using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>The world says when what it shows changes - entities joining and leaving it, owners, components and anything
/// of theirs - so a universe drawing on demand wakes for it, whoever made the change.</summary>
[TestFixture]
public class WorldChangedTests
{
    private EntityWorld world;
    private List<UniverseChangedEventArgs> changes;

    [SetUp]
    public void SetUp()
    {
        world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        changes = [];
        world.UniverseChanged += OnUniverseChanged;
    }

    [TearDown]
    public void TearDown()
    {
        world.UniverseChanged -= OnUniverseChanged;
    }

    private void OnUniverseChanged(object sender, UniverseChangedEventArgs change)
    {
        changes.Add(change);
    }

    [Test]
    public void AnEntityAddedOrRemoved_ChangesTheWorld()
    {
        var model = new Entity(null, "Model");

        world.EntityManager.AddEntity(model);
        var added = changes.Count;
        world.ForceUpdate();
        changes.Clear();
        world.EntityManager.RemoveEntity(model);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.Positive);
            Assert.That(changes, Is.Not.Empty);
        });
    }

    [Test]
    public void APropertyOfAComponentDeepInATree_ChangesTheWorld()
    {
        var model = new Entity(null, "Model");
        var lamp = new Entity(new Entity(model, "Body"), "Lamp");
        var light = new Light();
        lamp.AddComponent(light);
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        changes.Clear();

        light.Intensity = 3;

        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].Frames, Is.EqualTo(1));
    }

    [Test]
    public void AnEntityOutsideTheWorld_ChangesNothing_NorDoesOneTakenOut()
    {
        var stray = new Entity(null, "Stray");
        var model = new Entity(null, "Model");
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        world.EntityManager.RemoveEntity(model);
        world.ForceUpdate();
        changes.Clear();

        stray.Name = "Still a stray";
        stray.AddComponent(new Light());
        model.Name = "Gone";
        model.Transform.Position = new Vector3<double>(1, 2, 3);

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void AComponentAddedOrRemoved_ChangesTheWorld()
    {
        var model = new Entity(null, "Model");
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        var light = new Light();

        changes.Clear();
        model.AddComponent(light);
        var added = changes.Count;
        changes.Clear();
        model.RemoveComponent(light);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.Positive);
            Assert.That(changes, Is.Not.Empty);
        });
    }

    [Test]
    public void AnEntityHungUnderOneOfTheWorld_ChangesIt()
    {
        var model = new Entity(null, "Model");
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        var part = new Entity(null, "Part");
        changes.Clear();

        world.EntityManager.SetOwner(part, model);
        var hung = changes.Count;
        changes.Clear();
        part.Name = "Wing";

        Assert.Multiple(() =>
        {
            Assert.That(hung, Is.Positive);
            Assert.That(changes, Has.Count.EqualTo(1), "a part hung under the world's entity is the world's");
        });
    }

    [Test]
    public void ACameraFlight_KeepsTheWorldChangingAsLongAsItFlies()
    {
        var holder = new Entity(null, "Camera");
        var camera = new Camera(60, 800, 600, 0.1f, 1000f);
        holder.AddComponent(camera);
        world.EntityManager.AddEntity(holder);
        world.ForceUpdate();
        changes.Clear();

        camera.MoveTo(new Vector3<double>(5, 0, 0), 500);

        Assert.That(changes, Has.Some.Matches<UniverseChangedEventArgs>(change => change.Duration == TimeSpan.FromMilliseconds(500)));
    }
}
