using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class TriangleTreeTests
{
    [Test]
    public void RandomSoup_HitsWhatTestingEveryTriangleHits()
    {
        var random = new Random(7);
        var points = new Vector3<double>[3000 * 3];
        for (int i = 0; i < points.Length; i += 3)
        {
            var center = new Vector3<double>(Next(random) * 50, Next(random) * 50, Next(random) * 50);
            for (int k = 0; k < 3; k++)
            {
                points[i + k] = center + new Vector3<double>(Next(random) * 3, Next(random) * 3, Next(random) * 3);
            }
        }

        var tree = new TriangleTree(points, [], PrimitiveType.TriangleList);

        for (int i = 0; i < 1000; i++)
        {
            var from = new Vector3F(Next(random), Next(random), Next(random)) * 120;
            var to = new Vector3F(Next(random), Next(random), Next(random)) * 40;
            var ray = new Ray<float>(from, Vector3F.Normalize(to - from));

            var expected = EveryTriangle(points, ray, out var expectedDistance);
            var found = tree.Intersect(ray, out var distance);

            Assert.That(found, Is.EqualTo(expected), $"ray {i}");
            if (expected)
            {
                Assert.That(distance, Is.EqualTo(expectedDistance), $"ray {i}");
            }
        }
    }

    [Test]
    public void Strip_SkipsWhatARestartSeparates()
    {
        Vector3<double>[] points =
        [
            new(-1, -1, 10), new(-1, 1, 10), new(1, -1, 10), new(1, 1, 10),
            new(4, -1, 20), new(4, 1, 20), new(6, -1, 20), new(6, 1, 20)
        ];
        var tree = new TriangleTree(points, [0, 1, 2, 3, -1, 4, 5, 6, 7], PrimitiveType.TriangleStrip);

        Assert.That(tree.TriangleCount, Is.EqualTo(4));
        Assert.That(tree.Intersect(new Ray<float>(new Vector3F(5, 0, 0), Vector3F.UnitZ), out var distance), Is.True);
        Assert.That(distance, Is.EqualTo(20f).Within(1e-4f));
        Assert.That(tree.Intersect(new Ray<float>(new Vector3F(2.5f, 0, 0), Vector3F.UnitZ), out _), Is.False);
    }

    [Test]
    public void Mesh_KeepsItsTree_UntilItsPointsMove()
    {
        var mesh = new Mesh(PrimitiveType.TriangleList)
            .SetPoints([new Vector3<double>(-1, -1, 10), new Vector3<double>(1, -1, 10), new Vector3<double>(0, 1, 10)])
            .SetIndices([0, 1, 2]);
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.UnitZ);

        var first = mesh.GetTriangleTree();
        Assert.That(mesh.GetTriangleTree(), Is.SameAs(first));

        mesh.ApplyTransform(Matrix4x4.CreateTranslation<double>(0, 0, 5));

        Assert.That(mesh.GetTriangleTree(), Is.Not.SameAs(first));
        Assert.That(mesh.GetTriangleTree().Intersect(ray, out var distance), Is.True);
        Assert.That(distance, Is.EqualTo(15f).Within(1e-4f));
    }

    [Test]
    public void Lines_HaveNoTriangles()
    {
        var tree = new TriangleTree([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0)], [], PrimitiveType.LineList);

        Assert.That(tree.TriangleCount, Is.Zero);
        Assert.That(tree.Intersect(new Ray<float>(Vector3F.Zero, Vector3F.UnitX), out _), Is.False);
    }

    private static float Next(Random random)
    {
        return (float)(random.NextDouble() * 2 - 1);
    }

    private static bool EveryTriangle(Vector3<double>[] points, Ray<float> ray, out float nearest)
    {
        nearest = float.MaxValue;
        var found = false;
        for (int i = 0; i < points.Length; i += 3)
        {
            var a = points[i].As<float>();
            var b = points[i + 1].As<float>();
            var c = points[i + 2].As<float>();
            if (Collision.RayIntersectsTriangle(ray, a, b, c, out float distance) && distance < nearest)
            {
                nearest = distance;
                found = true;
            }
        }

        return found;
    }
}
