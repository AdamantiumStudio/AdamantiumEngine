using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The scene's index of components: what a service asks for instead of walking every tree - kept as trees come
/// and go, as components are added and taken off, skipping disabled entities, and walked without allocating.</summary>
[TestFixture]
public class SceneComponentsTests
{
    private TestUniverse _universe;
    private EntityWorld _world;
    private EntityManager _manager;

    [SetUp]
    public void MakeWorld()
    {
        _universe = new TestUniverse();
        _world = _universe.EntityWorld;
        _manager = _world.EntityManager;
    }

    [Test]
    public void ATreeComingIntoTheScene_BringsTheComponentsOfAllItsEntities()
    {
        var root = Marked("Root");
        var child = Marked("Child", root);
        var plain = new Entity(root, "Plain");

        _manager.AddEntity(root);
        NextFrame();

        Assert.Multiple(() =>
        {
            Assert.That(Collect(_world.MainScene.Components.Of<Marker>()), Is.EquivalentTo(new[] { root, child }));
            Assert.That(Collect(_world.MainScene.Components.Of<Transform>()), Is.EquivalentTo(new[] { root, child, plain }));
        });
    }

    [Test]
    public void AComponentAddedOrTakenOff_InTheScene_IsFollowedAtOnce()
    {
        var entity = new Entity(null, "Entity");
        _manager.AddEntity(entity);
        NextFrame();
        var index = _world.MainScene.Components;

        var marker = new Marker();
        entity.AddComponent(marker);
        var added = Collect(index.Of<Marker>());
        entity.RemoveComponent(marker);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.EqualTo(new[] { entity }));
            Assert.That(Collect(index.Of<Marker>()), Is.Empty);
        });
    }

    [Test]
    public void ATreeLeavingTheScene_TakesItsComponentsOut_AndIsNotFollowedAnyMore()
    {
        var root = Marked("Root");
        var child = Marked("Child", root);
        _manager.AddEntity(root);
        NextFrame();

        _manager.RemoveEntity(root);
        NextFrame();
        child.AddComponent(new Light());

        var index = _world.MainScene.Components;
        Assert.Multiple(() =>
        {
            Assert.That(index.CountOf<Marker>(), Is.Zero);
            Assert.That(index.CountOf<Light>(), Is.Zero);
        });
    }

    // Templates hang icons and frames straight under entities already in a scene.
    [Test]
    public void ABranchHungUnderAnEntityOfTheScene_JoinsAtOnce()
    {
        var root = new Entity(null, "Root");
        _manager.AddEntity(root);
        NextFrame();

        var branch = Marked("Branch", root);

        Assert.That(Collect(_world.MainScene.Components.Of<Marker>()), Is.EqualTo(new[] { branch }));
    }

    [Test]
    public void ATreeMovedToAnotherScene_MovesToItsIndex()
    {
        var other = _world.CreateScene("Other");
        var root = Marked("Root");
        _manager.AddEntity(root);
        NextFrame();

        _manager.MoveToScene(root, other);
        NextFrame();

        Assert.Multiple(() =>
        {
            Assert.That(_world.MainScene.Components.CountOf<Marker>(), Is.Zero);
            Assert.That(Collect(other.Components.Of<Marker>()), Is.EqualTo(new[] { root }));
        });
    }

    [Test]
    public void AnEntityGivenAnOwnerInAnotherScene_MovesToThatScenesIndex()
    {
        var other = _world.CreateScene("Other");
        var owner = new Entity(null, "Owner");
        var entity = Marked("Entity");
        _manager.AddEntity(owner, other);
        _manager.AddEntity(entity);
        NextFrame();

        _manager.SetOwner(entity, owner);
        NextFrame();

        Assert.Multiple(() =>
        {
            Assert.That(_world.MainScene.Components.CountOf<Marker>(), Is.Zero);
            Assert.That(Collect(other.Components.Of<Marker>()), Is.EqualTo(new[] { entity }));
        });
    }

    [Test]
    public void ADisabledEntity_AndAllUnderIt_AreLeftOutOfSelections_TillEnabledAgain()
    {
        var root = Marked("Root");
        var child = Marked("Child", root);
        var grandchild = Marked("Grandchild", child);
        _manager.AddEntity(root);
        NextFrame();
        var index = _world.MainScene.Components;

        child.IsEnabled = false;
        var whileDisabled = Collect(index.Of<Marker>());
        child.IsEnabled = true;

        Assert.Multiple(() =>
        {
            Assert.That(whileDisabled, Is.EqualTo(new[] { root }));
            Assert.That(grandchild.IsEnabledInTree, Is.True);
            Assert.That(Collect(index.Of<Marker>()), Is.EquivalentTo(new[] { root, child, grandchild }));
            Assert.That(index.CountOf<Marker>(), Is.EqualTo(3), "the disabled are kept, only not selected");
        });
    }

    [Test]
    public void AnEntityHungUnderADisabledOne_IsDisabledInItsTree()
    {
        var owner = new Entity(null, "Owner") { IsEnabled = false };

        var child = new Entity(owner, "Child");

        Assert.That(child.IsEnabledInTree, Is.False);
    }

    [Test]
    public void ASelectionOfABaseType_HoldsEveryDerivedType()
    {
        var marked = Marked("Marked");
        var lit = new Entity(null, "Lit");
        lit.AddComponent(new Light());
        var seeing = new Entity(null, "Seeing");
        seeing.AddComponent(new Camera());
        _manager.AddEntity(marked);
        _manager.AddEntity(lit);
        _manager.AddEntity(seeing);
        NextFrame();
        var index = _world.MainScene.Components;

        Assert.Multiple(() =>
        {
            Assert.That(Collect(index.Of<ActivatableComponent>()), Is.SupersetOf(new[] { marked, lit }));
            Assert.That(Collect(index.Of<CameraBase>()), Is.EqualTo(new[] { seeing }));
        });
    }

    [Test]
    public void AComponentTakenOutDuringAWalk_IsNotWalked_AndTheWalkGoesOn()
    {
        var entities = new List<Entity>();
        for (var i = 0; i < 6; i++)
        {
            var entity = Marked($"Entity {i}");
            entities.Add(entity);
            _manager.AddEntity(entity);
        }

        NextFrame();
        var index = _world.MainScene.Components;
        var walked = new List<Entity>();
        var added = Marked("Added");

        foreach (var marker in index.Of<Marker>())
        {
            walked.Add(marker.Owner);
            if (walked.Count == 1)
            {
                // Taken out ahead of the walk, and one comes in.
                foreach (var other in entities)
                {
                    if (other != marker.Owner && entities.IndexOf(other) % 2 == 0)
                        other.RemoveComponent<Marker>();
                }

                _manager.AddEntity(added);
                NextFrame();
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(walked, Has.None.Matches<Entity>(entity => !entity.ContainsComponent<Marker>()));
            Assert.That(walked, Does.Not.Contain(added), "what comes in during a walk waits for the next one");
            Assert.That(Collect(index.Of<Marker>()), Does.Contain(added));
            Assert.That(index.CountOf<Marker>(), Is.EqualTo(Collect(index.Of<Marker>()).Count), "the gaps are closed");
        });
    }

    [Test]
    public void APairSelection_GivesTheEntitiesWithBoth()
    {
        var both = Marked("Both");
        both.AddComponent(new Light());
        var markedOnly = Marked("Marked");
        var litOnly = new Entity(null, "Lit");
        litOnly.AddComponent(new Light());
        _manager.AddEntity(both);
        _manager.AddEntity(markedOnly);
        _manager.AddEntity(litOnly);
        NextFrame();

        var pairs = new List<Entity>();
        foreach (var pair in _world.MainScene.Components.With<Marker, Light>())
        {
            Assert.That(pair.First, Is.SameAs(pair.Entity.GetComponent<Marker>()));
            Assert.That(pair.Second, Is.SameAs(pair.Entity.GetComponent<Light>()));
            pairs.Add(pair.Entity);
        }

        Assert.That(pairs, Is.EqualTo(new[] { both }));
    }

    // The scene's cameras group missed a camera given to an entity already in the scene.
    [Test]
    public void ACameraGivenToAnEntityOfTheScene_JoinsTheScenesCameras_AndLeavesWhenTakenOff()
    {
        var entity = new Entity(null, "Entity");
        _manager.AddEntity(entity);
        NextFrame();
        var camera = new Camera();

        entity.AddComponent(camera);
        var joined = _world.MainScene.Cameras.Contains(entity);
        entity.RemoveComponent(camera);

        Assert.Multiple(() =>
        {
            Assert.That(joined, Is.True);
            Assert.That(_world.MainScene.Cameras.Contains(entity), Is.False);
        });
    }

    [Test]
    public void SelectionsAndLookups_AllocateNothing()
    {
        for (var i = 0; i < 100; i++)
        {
            var entity = Marked($"Entity {i}");
            entity.AddComponent(new Light());
            _manager.AddEntity(entity);
        }

        NextFrame();
        var index = _world.MainScene.Components;
        var count = Walk(index);

        var before = GC.GetAllocatedBytesForCurrentThread();
        count += Walk(index);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(600));
            Assert.That(allocated, Is.Zero);
        });
    }

    [Test]
    public void ALookup_FindsADerivedType_AndFollowsTheComponentsChanging()
    {
        var entity = new Entity(null, "Entity");
        var missingBefore = entity.GetComponent<CameraBase>();
        var camera = new Camera();

        entity.AddComponent(camera);
        var found = entity.GetComponent<CameraBase>();
        entity.RemoveComponent(camera);

        Assert.Multiple(() =>
        {
            Assert.That(missingBefore, Is.Null);
            Assert.That(found, Is.SameAs(camera));
            Assert.That(entity.GetComponent<CameraBase>(), Is.Null);
        });
    }

    // Cleared, every component was not told and let go: the loop stepped over every second one.
    [Test]
    public void ClearedComponents_AreEachTakenOff_AndToldOf()
    {
        var entity = new Entity(null, "Entity");
        entity.AddComponent(new Marker());
        entity.AddComponent(new Light());
        var removed = 0;
        entity.ComponentsChanged += (_, change) =>
        {
            if (change.Action == ComponentChangedAction.Removed)
                removed++;
        };

        entity.Components.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(3));
            Assert.That(entity.Components, Is.Empty);
        });
    }

    [Test]
    public void AResetWorld_EmptiesTheIndex()
    {
        var root = Marked("Root");
        _manager.AddEntity(root);
        NextFrame();

        _manager.Reset();
        root.AddComponent(new Light());

        Assert.Multiple(() =>
        {
            Assert.That(_world.MainScene.Components.CountOf<Marker>(), Is.Zero);
            Assert.That(_world.MainScene.Components.CountOf<Light>(), Is.Zero, "no longer followed");
        });
    }

    private static int Walk(SceneComponents index)
    {
        var count = 0;
        foreach (var marker in index.Of<Marker>())
        {
            if (marker.Owner.GetComponent<Light>() != null)
                count++;
        }

        foreach (var pair in index.With<Marker, Light>())
        {
            if (pair.Second != null)
                count++;
        }

        foreach (var light in index.Of<Light>())
        {
            if (light.Owner.GetComponent<Marker>() != null)
                count++;
        }

        return count;
    }

    private static Entity Marked(string name, Entity owner = null)
    {
        var entity = new Entity(owner, name);
        entity.AddComponent(new Marker());
        return entity;
    }

    private static List<Entity> Collect<T>(ComponentSelection<T> selection) where T : class, IComponent
    {
        var owners = new List<Entity>();
        foreach (var component in selection)
        {
            owners.Add(((IEntityOwner)component).Owner);
        }

        return owners;
    }

    private void NextFrame()
    {
        _world.ForceUpdate();
        _universe.Settle();
    }
}
