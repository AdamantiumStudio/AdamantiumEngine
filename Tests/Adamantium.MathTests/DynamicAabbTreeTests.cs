using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

public class DynamicAabbTreeTests
{
    /// <summary>Whatever was added, moved and removed, a ray finds exactly the items whose leaf boxes it passes through -
    /// the ones a look at every leaf finds - and enters each where that look says.</summary>
    [Test]
    public void ARay_FindsWhatALookAtEveryLeafFinds()
    {
        var random = new Random(7);
        var tree = new DynamicAabbTree<int>();
        var handles = new Dictionary<int, int>();
        for (int i = 0; i < 500; i++)
        {
            handles[i] = tree.Add(RandomBox(random), i);
        }

        for (int step = 0; step < 2000; step++)
        {
            var item = random.Next(500);
            if (!handles.TryGetValue(item, out var handle))
            {
                handles[item] = tree.Add(RandomBox(random), item);
            }
            else if (random.Next(4) == 0)
            {
                tree.Remove(handle);
                handles.Remove(item);
            }
            else
            {
                tree.Move(handle, RandomBox(random));
            }
        }

        for (int r = 0; r < 200; r++)
        {
            var ray = new Ray<float>(RandomPoint(random) * 1.5f, Vector3F.Normalize(RandomPoint(random)));
            var found = new List<(int Item, float Entry)>();
            tree.RayCast(ray, found);

            var expected = new List<(int Item, float Entry)>();
            foreach (var (item, handle) in handles)
            {
                var box = tree.BoxOf(handle);
                if (Collision.RayIntersectsBox(ray, box, out float entry))
                {
                    expected.Add((item, entry));
                }
            }

            Assert.That(found.OrderBy(f => f.Item), Is.EqualTo(expected.OrderBy(e => e.Item)));
        }

        Assert.That(tree.Count, Is.EqualTo(handles.Count));
    }

    /// <summary>Items added in a row along a line - the order that turns a tree without rotations into a list - still
    /// make a tree of logarithmic height.</summary>
    [Test]
    public void ItemsAddedInARow_KeepTheTreeShallow()
    {
        var tree = new DynamicAabbTree<int>();
        for (int i = 0; i < 1024; i++)
        {
            tree.Add(new BoundingBox<float>(new Vector3F(i, 0, 0), new Vector3F(i + 0.5f, 0.5f, 0.5f)), i);
        }

        Assert.That(tree.Height, Is.LessThanOrEqualTo(2 * (int)Math.Ceiling(Math.Log2(1024)) + 1));
    }

    /// <summary>A move that stays within the margin leaves the leaf where it is; one past it moves the leaf, whose box
    /// then holds the new one.</summary>
    [Test]
    public void ALeaf_MovesOnlyOnceItsItemLeavesItsBox()
    {
        var tree = new DynamicAabbTree<string>();
        var handle = tree.Add(new BoundingBox<float>(Vector3F.Zero, Vector3F.One), "a");

        Assert.That(tree.Move(handle, new BoundingBox<float>(new Vector3F(0.05f), new Vector3F(1.05f))), Is.False);

        var far = new BoundingBox<float>(new Vector3F(5), new Vector3F(6));
        Assert.That(tree.Move(handle, far), Is.True);
        Assert.That(tree.BoxOf(handle).Contains(far), Is.EqualTo(ContainmentType.Contains));
    }

    private static BoundingBox<float> RandomBox(Random random)
    {
        var center = RandomPoint(random) * 10;
        var half = new Vector3F((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()) * 0.5f;
        return new BoundingBox<float>(center - half, center + half);
    }

    private static Vector3F RandomPoint(Random random) =>
        new((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1));
}
