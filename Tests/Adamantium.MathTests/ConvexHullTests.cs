using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class ConvexHullTests
{
    [Test]
    public void TheVolume_IsTheSpaceTheHullHolds_AndNoneForAFlatOne()
    {
        var box = ConvexHull.FromPoints([
            new Vector3F(0, 0, 0), new Vector3F(2, 0, 0), new Vector3F(0, 3, 0), new Vector3F(2, 3, 0),
            new Vector3F(0, 0, 4), new Vector3F(2, 0, 4), new Vector3F(0, 3, 4), new Vector3F(2, 3, 4),
            new Vector3F(1, 1, 1)]);
        var tetrahedron = ConvexHull.FromPoints([
            new Vector3F(0, 0, 0), new Vector3F(1, 0, 0), new Vector3F(0, 1, 0), new Vector3F(0, 0, 1)]);
        var flat = ConvexHull.FromPoints([
            new Vector3F(0, 0, 0), new Vector3F(1, 0, 0), new Vector3F(0, 0, 1), new Vector3F(1, 0, 1)]);

        Assert.Multiple(() =>
        {
            Assert.That(box.MeasureVolume(), Is.EqualTo(24).Within(1e-9));
            Assert.That(tetrahedron.MeasureVolume(), Is.EqualTo(1.0 / 6).Within(1e-9));
            Assert.That(flat.MeasureVolume(), Is.Zero);
        });
    }

    [Test]
    public void BoxCorners_WithPointsInside_GiveTheBox()
    {
        var random = new Random(21);
        List<Vector3F> points = [.. Corners(new BoundingBox<float>(new Vector3F(-1, -2, -3), new Vector3F(1, 2, 3)))];
        for (int i = 0; i < 200; i++)
        {
            points.Add(new Vector3F(Next(random), Next(random) * 2, Next(random) * 3) * 0.9f);
        }

        var hull = ConvexHull.FromPoints(points);

        Assert.That(hull.Vertices.Count, Is.EqualTo(8));
        Assert.That(hull.Triangles.Count, Is.EqualTo(12 * 3));
        Assert.That(points.All(hull.Contains), Is.True);
    }

    // A hull a file keeps comes back as it was built, its planes and containment the same; what cannot be a solid hull's
    // comes back as none.
    [Test]
    public void AHullBuiltAlready_IsTakenAsItIs_AndNonsenseIsRefused()
    {
        var built = ConvexHull.FromPoints([.. Corners(new BoundingBox<float>(new Vector3F(-1), new Vector3F(1)))]);

        var taken = ConvexHull.FromBuilt(built.Vertices, built.Triangles);

        Assert.That(taken.Vertices, Is.EqualTo(built.Vertices));
        Assert.That(taken.Triangles, Is.EqualTo(built.Triangles));
        Assert.That(taken.Planes, Has.Count.EqualTo(built.Planes.Count));
        Assert.That(taken.Contains(Vector3F.Zero), Is.True);
        Assert.That(taken.Contains(new Vector3F(1.5f, 0, 0)), Is.False);
        Assert.That(ConvexHull.FromBuilt(built.Vertices, [0, 1, 2]), Is.Null, "too few triangles");
        Assert.That(ConvexHull.FromBuilt(built.Vertices, [.. built.Triangles.Take(35), 99]), Is.Null, "a corner out of range");
        Assert.That(ConvexHull.FromBuilt(null, built.Triangles), Is.Null);
        var inward = built.Triangles.ToArray();
        for (var corner = 0; corner < inward.Length; corner += 3)
        {
            (inward[corner + 1], inward[corner + 2]) = (inward[corner + 2], inward[corner + 1]);
        }

        Assert.That(ConvexHull.FromBuilt(built.Vertices, inward), Is.Null, "wound inward");
        Assert.That(ConvexHull.FromBuilt(built.Vertices, [.. built.Triangles.Skip(6)]), Is.Null, "torn open");
        Assert.That(ConvexHull.FromBuilt([.. built.Vertices, new Vector3F(100, 0, 0)], built.Triangles), Is.Null,
            "a corner no triangle uses");
        var dented = built.Vertices.ToArray();
        dented[0] *= 0.5f;
        Assert.That(ConvexHull.FromBuilt(dented, built.Triangles), Is.Null, "not convex");

        // A double pyramid on a hexagon whose rim is listed twice over: closed, convex at every edge, and wrapped round
        // twice.
        List<Vector3F> twice = [new Vector3F(0, -1, 0), new Vector3F(0, 1, 0)];
        for (var corner = 0; corner < 12; corner++)
        {
            var angle = corner % 6 * MathF.PI / 3;
            twice.Add(new Vector3F(MathF.Cos(angle), 0, MathF.Sin(angle)));
        }

        List<int> twiceTriangles = [];
        for (var corner = 0; corner < 12; corner++)
        {
            var rim = 2 + corner;
            var next = 2 + (corner + 1) % 12;
            twiceTriangles.AddRange([0, rim, next, 1, next, rim]);
        }

        Assert.That(ConvexHull.FromBuilt(twice, twiceTriangles), Is.Null, "wrapped round twice");
    }

    // Every hull Quickhull builds is one FromBuilt takes back: a file's hull is never refused for being a hull.
    [Test]
    public void EveryHullQuickhullBuilds_IsTakenBack([Range(0, 19)] int seed)
    {
        var random = new Random(seed);
        var count = 10 + seed * 50;
        var points = Enumerable.Range(0, count)
            .Select(_ => seed % 2 == 0
                ? Vector3F.Normalize(new Vector3F(Next(random), Next(random), Next(random)) + new Vector3F(1e-4f)) * (1 + seed)
                : new Vector3F(Next(random), Next(random) * 0.3f, Next(random)) + new Vector3F(500, 0, 0))
            .ToList();
        var built = ConvexHull.FromPoints(points);

        Assert.That(ConvexHull.FromBuilt(built.Vertices, built.Triangles), Is.Not.Null);
    }

    [Test]
    public void PointsOnASphere_GiveAClosedConvexHull()
    {
        var random = new Random(22);
        var points = Enumerable.Range(0, 400)
            .Select(_ => Vector3F.Normalize(new Vector3F(Next(random), Next(random), Next(random)) + new Vector3F(1e-4f)) * 3)
            .ToList();

        var hull = ConvexHull.FromPoints(points);

        Assert.That(points.All(hull.Contains), Is.True, "every point is inside");
        foreach (var plane in hull.Planes)
        {
            foreach (var vertex in hull.Vertices)
            {
                Assert.That(Plane<float>.DotCoordinate(plane, vertex), Is.LessThan(1e-3f), "no corner stands outside a face");
            }
        }

        var faces = hull.Triangles.Count / 3;
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < hull.Triangles.Count; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                var a = hull.Triangles[i + k];
                var b = hull.Triangles[i + (k + 1) % 3];
                Assert.That(edges.Add((a, b)), Is.True, "each directed edge once: the surface is closed and wound one way");
            }
        }

        Assert.That(edges.All(edge => edges.Contains((edge.Item2, edge.Item1))), Is.True, "every edge has its neighbor");
        Assert.That(hull.Vertices.Count - edges.Count / 2 + faces, Is.EqualTo(2), "Euler: V - E + F = 2");
    }

    [Test]
    public void ARay_MeetsTheHullWhereItMeetsTheSameBox()
    {
        var random = new Random(23);
        var box = new OrientedBoundingBox<float>(new Vector3F(1, 2, 3), new Vector3F(1, 0.5f, 2), Quaternion.CreateFromAxisAngle<float>(Vector3F.Normalize(new Vector3F(1, 1, 0)), 0.7f));
        var hull = ConvexHull.FromPoints(Corners(box));

        for (int i = 0; i < 500; i++)
        {
            var from = new Vector3F(Next(random), Next(random), Next(random)) * 8;
            var ray = new Ray<float>(from, Vector3F.Normalize(box.Center + new Vector3F(Next(random), Next(random), Next(random)) * 2 - from));

            var expected = box.Intersects(ray, out float boxDistance);
            Assert.That(hull.Intersects(ray, out float hullDistance), Is.EqualTo(expected), $"ray {i}");
            if (expected)
            {
                Assert.That(hullDistance, Is.EqualTo(boxDistance).Within(1e-3f), $"ray {i}");
            }
        }
    }

    [Test]
    public void AFlatSet_GivesAPolygon_ThatRaysHitOnlyInside()
    {
        List<Vector3F> points = [new(-1, -1, 5), new(1, -1, 5), new(1, 1, 5), new(-1, 1, 5), new(0, 0, 5), new(0.5f, -0.2f, 5)];

        var hull = ConvexHull.FromPoints(points);

        Assert.That(hull.IsFlat, Is.True);
        Assert.That(hull.Vertices.Count, Is.EqualTo(4));
        var through = new Ray<float>(new Vector3F(0.3f, 0.4f, 0), Vector3F.UnitZ);
        var beside = new Ray<float>(new Vector3F(1.5f, 0, 0), Vector3F.UnitZ);
        var along = new Ray<float>(new Vector3F(-5, 0, 5), Vector3F.UnitX);
        Assert.That(hull.Intersects(through, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(5f).Within(1e-4f));
        Assert.That(hull.Intersects(beside, out float _), Is.False);
        Assert.That(hull.Intersects(along, out float edge), Is.True);
        Assert.That(edge, Is.EqualTo(4f).Within(1e-4f));
    }

    [Test]
    public void PointsOnALine_GiveCornersARayNeverMeets()
    {
        var hull = ConvexHull.FromPoints([new Vector3F(0, 0, 0), new Vector3F(1, 0, 0), new Vector3F(3, 0, 0)]);
        var ray = new Ray<float>(new Vector3F(1, -5, 0), Vector3F.UnitY);

        Assert.That(hull.Vertices.Count, Is.EqualTo(2));
        Assert.That(hull.Intersects(ray, out float _), Is.False);
        Assert.That(hull.Support(Vector3F.UnitX).X, Is.EqualTo(3f));
    }

    [Test]
    public void AMirroringTransform_KeepsTheFacesTurnedOut()
    {
        var hull = ConvexHull.FromPoints(Corners(new BoundingBox<float>(Vector3F.Zero, Vector3F.One)));

        var mirrored = hull.Transform(Matrix4x4.CreateScale<float>(-1, 1, 1) * Matrix4x4.CreateTranslation<float>(5, 0, 0));

        Assert.That(mirrored.Contains(new Vector3F(4.5f, 0.5f, 0.5f)), Is.True);
        Assert.That(mirrored.Contains(new Vector3F(5.5f, 0.5f, 0.5f)), Is.False);
    }

    [Test]
    public void AHull_BehavesLikeItsBox_UnderGjk()
    {
        var random = new Random(24);
        var box = new OrientedBoundingBox<float>(Vector3F.Zero, new Vector3F(1, 0.5f, 2), Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, 0.4f));
        var hull = ConvexHull.FromPoints(Corners(box));

        for (int i = 0; i < 1000; i++)
        {
            var center = new Vector3F(Next(random), Next(random), Next(random)) * 4;
            var radius = 0.2f + (float)random.NextDouble();
            var shrunk = new BoundingSphere<float>(center, radius - 1e-3f);
            var grown = new BoundingSphere<float>(center, radius + 1e-3f);
            var expected = box.Intersects(shrunk);
            if (expected != box.Intersects(grown))
            {
                continue;
            }

            Assert.That(Gjk.Intersects(hull, new BoundingSphere<float>(center, radius)), Is.EqualTo(expected), $"sphere {i}");
        }
    }

    private static float Next(Random random)
    {
        return (float)(random.NextDouble() * 2 - 1);
    }

    private static Vector3F[] Corners(OrientedBoundingBox<float> box)
    {
        var corners = new Vector3F[OrientedBoundingBox<float>.CornerCount];
        box.GetCorners(corners);
        return corners;
    }

    private static Vector3F[] Corners(BoundingBox<float> box) => Corners(OrientedBoundingBox.CreateFromBox(box));
}
