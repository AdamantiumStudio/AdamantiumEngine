using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>How much a selection from the scene's index costs against walking every tree: 10 000 entities, one in a
/// hundred with the component asked for. Run by hand; prints the times and the bytes allocated.</summary>
[TestFixture]
[Explicit("A measurement, run by hand")]
public class SceneComponentsMeasurement
{
    private const int Entities = 10_000;
    private const int Rounds = 200;

    [Test]
    public void SelectionAgainstTreeWalk()
    {
        var universe = new TestUniverse();
        var world = universe.EntityWorld;
        for (var i = 0; i < Entities / 10; i++)
        {
            var root = new Entity(null, $"Root {i}");
            for (var j = 0; j < 9; j++)
            {
                var child = new Entity(root, $"Child {j}");
                child.AddComponent(new Light());
                if ((i * 10 + j) % 100 == 0)
                    child.AddComponent(new Marker());
            }

            world.EntityManager.AddEntity(root);
        }

        world.ForceUpdate();
        universe.Settle();
        var scene = world.MainScene;

        var walked = Measure("tree walk, Stack per call", () => WalkTrees(scene), out var walkBytes);
        var selected = Measure("Scene.Components.Of<Marker>()", () => Select(scene), out var selectBytes);

        Assert.That(selected, Is.EqualTo(walked));
        TestContext.Out.WriteLine($"allocated per round: walk {walkBytes / Rounds} B, selection {selectBytes / Rounds} B");
    }

    // An entity's lookup by type, kept, against the scan GetComponent did before, over entities of six components.
    [Test]
    public void LookupAgainstScan()
    {
        var entities = new List<Entity>();
        for (var i = 0; i < Entities; i++)
        {
            var entity = new Entity(null, $"Entity {i}");
            entity.AddComponent(new Light());
            entity.AddComponent(new Camera());
            entity.AddComponent(new BoxCollider());
            entity.AddComponent(new MeshData());
            entity.AddComponent(new Marker());
            entities.Add(entity);
        }

        var scanned = Measure("scan of the components", () => Scan(entities), out _);
        var looked = Measure("GetComponent<Marker>()", () => Look(entities), out var lookBytes);

        Assert.That(looked, Is.EqualTo(scanned));
        TestContext.Out.WriteLine($"allocated per round by the lookup: {lookBytes / Rounds} B");
    }

    private static int Scan(List<Entity> entities)
    {
        var count = 0;
        foreach (var entity in entities)
        {
            foreach (var component in entity.Components.AsSpan())
            {
                if (component is Marker)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static int Look(List<Entity> entities)
    {
        var count = 0;
        foreach (var entity in entities)
        {
            if (entity.GetComponent<Marker>() != null)
                count++;
        }

        return count;
    }

    private static int Measure(string what, Func<int> round, out long allocated)
    {
        var found = round();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Rounds; i++)
        {
            found = round();
        }

        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"{what}: {clock.Elapsed.TotalMilliseconds * 1000 / Rounds:F1} us a round, {found} found");
        return found;
    }

    // What GameService.Each<T>() did: every tree, every entity asked.
    private static int WalkTrees(Scene scene)
    {
        var count = 0;
        var walk = new Stack<Entity>();
        foreach (var root in scene.RootEntities)
        {
            walk.Push(root);
        }

        while (walk.Count > 0)
        {
            var entity = walk.Pop();
            if (entity.GetComponent<Marker>() != null)
                count++;

            foreach (var child in entity.Dependencies)
            {
                walk.Push(child);
            }
        }

        return count;
    }

    private static int Select(Scene scene)
    {
        var count = 0;
        foreach (var marker in scene.Components.Of<Marker>())
        {
            if (marker != null)
                count++;
        }

        return count;
    }
}
