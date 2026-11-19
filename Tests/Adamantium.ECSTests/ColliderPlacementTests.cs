using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A collider placed on its entity by its position, rotation and scale; fitted to the mesh until a size or its
/// placement is set by hand.</summary>
[TestFixture]
public class ColliderPlacementTests
{
    [Test]
    public void ANewCollider_IsFitted_Unturned_AndUnscaled()
    {
        Collider[] colliders = [new BoxCollider(), new SphereCollider(), new CapsuleCollider(), new CylinderCollider(), new ConvexHullCollider()];

        Assert.Multiple(() =>
        {
            foreach (var collider in colliders)
            {
                Assert.That(collider.IsFitted, Is.True, collider.GetType().Name);
                Assert.That(collider.Rotation, Is.EqualTo(QuaternionF.Identity), collider.GetType().Name);
                Assert.That(collider.Scale, Is.EqualTo(Vector3F.One), collider.GetType().Name);
            }
        });
    }

    [Test]
    public void AFit_TakesTheMeshsCenterAndSize()
    {
        var box = With(new BoxCollider(), Shape(new Vector3F(1, 2, 3), new Vector3F(3, 6, 5)));

        Assert.Multiple(() =>
        {
            Assert.That(box.Position, Is.EqualTo(new Vector3F(2, 4, 4)));
            Assert.That(box.Size, Is.EqualTo(new Vector3F(2, 4, 2)));
            Assert.That(box.IsFitted, Is.True);
        });
    }

    [Test]
    public void ASizeSetByHand_StopsTheFit_AndANewMeshKeepsIt()
    {
        var entity = Shape(new Vector3F(-1), new Vector3F(1));
        var box = With(new BoxCollider(), entity);

        box.Size = new Vector3F(5);
        box.FollowMesh(Shape(new Vector3F(-3), new Vector3F(3)).GetComponent<MeshData>().Mesh);

        Assert.Multiple(() =>
        {
            Assert.That(box.IsFitted, Is.False);
            Assert.That(box.Size, Is.EqualTo(new Vector3F(5)));
            Assert.That(box.Bounds.Size.X, Is.EqualTo(5).Within(1e-5));
        });
    }

    [Test]
    public void FittedAgain_TheColliderTakesTheMeshAtOnce()
    {
        var box = With(new BoxCollider(), Shape(new Vector3F(-1), new Vector3F(1)));
        box.Size = new Vector3F(5);
        box.Position = new Vector3F(0, 3, 0);

        box.IsFitted = true;

        Assert.Multiple(() =>
        {
            Assert.That(box.Size, Is.EqualTo(new Vector3F(2)));
            Assert.That(box.Position, Is.EqualTo(Vector3F.Zero));
        });
    }

    [Test]
    public void AColliderSizedBeforeItIsGiven_KeepsItsSize()
    {
        var entity = Shape(new Vector3F(-1), new Vector3F(1));

        var capsule = With(new CapsuleCollider { Radius = 2, Height = 6 }, entity);

        Assert.Multiple(() =>
        {
            Assert.That(capsule.IsFitted, Is.False);
            Assert.That(capsule.Radius, Is.EqualTo(2));
            Assert.That(capsule.Capsule.Length, Is.EqualTo(2).Within(1e-5f));
        });
    }

    [Test]
    public void AScale_StretchesTheFit_AndKeepsItFitted()
    {
        var box = With(new BoxCollider(), Shape(new Vector3F(-1), new Vector3F(1)));

        box.Scale = new Vector3F(2, 1, 1);
        box.FollowMesh(Shape(new Vector3F(-2), new Vector3F(2)).GetComponent<MeshData>().Mesh);

        Assert.Multiple(() =>
        {
            Assert.That(box.IsFitted, Is.True);
            Assert.That(box.Size, Is.EqualTo(new Vector3F(4)));
            Assert.That(box.Bounds.Size.X, Is.EqualTo(8).Within(1e-5));
            Assert.That(box.Bounds.Size.Y, Is.EqualTo(4).Within(1e-5));
        });
    }

    [Test]
    public void APositionAndRotation_PlaceTheShape_WherePickingFindsIt()
    {
        var box = With(new BoxCollider(), Shape(new Vector3F(-1), new Vector3F(1)));
        box.Size = new Vector3F(4, 1, 1);

        box.Position = new Vector3F(0, 0, 10);
        box.Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, float.DegreesToRadians(90));
        var along = new Ray<float>(new Vector3F(0, 1.5f, -5), Vector3F.UnitZ);
        var beside = new Ray<float>(new Vector3F(1.5f, 0, -5), Vector3F.UnitZ);

        Assert.Multiple(() =>
        {
            Assert.That(box.Intersects(ref along, out var point), Is.True, "turned, the long side stands along Y");
            Assert.That(point.Z, Is.EqualTo(9.5f).Within(1e-4f));
            Assert.That(box.Intersects(ref beside, out _), Is.False);
            Assert.That(box.Bounds.Center.Z, Is.EqualTo(10).Within(1e-5));
        });
    }

    [Test]
    public void ACapsuleAndACylinder_StandWhereTheyArePlaced()
    {
        var capsule = With(new CapsuleCollider(), Shape(new Vector3F(-1), new Vector3F(1)));
        var cylinder = With(new CylinderCollider(), Shape(new Vector3F(-1), new Vector3F(1)));
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, float.DegreesToRadians(90));

        capsule.Height = 6;
        capsule.Radius = 1;
        capsule.Axis = ColliderAxis.Y;
        capsule.Rotation = turn;
        capsule.Position = new Vector3F(0, 5, 0);
        cylinder.Axis = ColliderAxis.Y;
        cylinder.Height = 4;
        cylinder.Position = new Vector3F(3, 0, 0);
        cylinder.Scale = new Vector3F(1, 2, 1);

        Assert.Multiple(() =>
        {
            Assert.That(capsule.Capsule.Center.Y, Is.EqualTo(5).Within(1e-5f));
            Assert.That(System.Math.Abs((capsule.Capsule.End - capsule.Capsule.Start).X), Is.EqualTo(4).Within(1e-4f),
                "turned to lie along X");
            Assert.That(cylinder.Cylinder.Center.X, Is.EqualTo(3).Within(1e-5f));
            Assert.That(cylinder.Cylinder.Length, Is.EqualTo(8).Within(1e-4f), "stretched along its axis");
        });
    }

    [Test]
    public void ACapsuleStretchedAlongItsAxis_GrowsLonger_NotThicker()
    {
        var capsule = With(new CapsuleCollider { Axis = ColliderAxis.Y, Radius = 0.5f, Height = 2 }, Shape(new Vector3F(-1), new Vector3F(1)));
        var cylinder = With(new CylinderCollider { Axis = ColliderAxis.Y, Radius = 0.5f, Height = 2 }, Shape(new Vector3F(-1), new Vector3F(1)));

        capsule.Scale = new Vector3F(1, 3, 1);
        cylinder.Scale = new Vector3F(1, 3, 2);

        Assert.Multiple(() =>
        {
            Assert.That(capsule.Capsule.Radius, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(capsule.Bounds.Size.Y, Is.EqualTo(6).Within(1e-4));
            Assert.That(cylinder.Cylinder.Radius, Is.EqualTo(1).Within(1e-5f), "the wider scale across");
            Assert.That(cylinder.Cylinder.Length, Is.EqualTo(6).Within(1e-4f));
        });
    }

    [Test]
    public void AnAxisGivenToACylinderThatKeepsItsAxis_IsFittedAlong()
    {
        var cylinder = With(new CylinderCollider { ChoosesAxis = false }, Shape(new Vector3F(-0.5f, -2, -0.5f), new Vector3F(0.5f, 2, 0.5f)));

        cylinder.Axis = ColliderAxis.X;

        Assert.Multiple(() =>
        {
            Assert.That(cylinder.IsFitted, Is.True);
            Assert.That(cylinder.Height, Is.EqualTo(1).Within(1e-5f), "the mesh's side along X");
            Assert.That(cylinder.Radius, Is.EqualTo(2).Within(1e-5f));
        });
    }

    [Test]
    public void ACylinderTurnedInTheSameBox_IsShownTurned()
    {
        var cylinder = With(new CylinderCollider { ChoosesAxis = false }, Shape(new Vector3F(-0.5f), new Vector3F(0.5f)));
        var shown = cylinder.GetVisualRepresentation();
        var standing = (double[])[.. System.Linq.Enumerable.Select(shown.Points, point => point.X)];

        cylinder.Axis = ColliderAxis.X;
        cylinder.GetVisualRepresentation();

        Assert.That(System.Linq.Enumerable.Select(shown.Points, point => point.X), Is.Not.EqualTo(standing),
            "the box round it is the same 1 by 1 by 1");
    }

    [Test]
    public void AColliderPlacedAnew_IsPlacedAnewWhereCamerasSeeIt()
    {
        var entity = Shape(new Vector3F(-1), new Vector3F(1));
        var box = With(new BoxCollider(), entity);
        entity.Transform.IsWorldDirty = false;

        box.Position = new Vector3F(10, 0, 0);

        Assert.That(entity.Transform.IsWorldDirty, Is.True);
    }

    [Test]
    public void ASphere_GrowsByItsLargestScale()
    {
        var sphere = With(new SphereCollider(), Shape(new Vector3F(-1), new Vector3F(1)));

        sphere.Radius = 2;
        sphere.Scale = new Vector3F(1, 3, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sphere.Sphere.Radius, Is.EqualTo(6).Within(1e-5f));
            Assert.That(sphere.Bounds.Size.Y, Is.EqualTo(12).Within(1e-4));
        });
    }

    [Test]
    public void AHullPlacedByHand_IsMoved_AndStillTakesANewMeshsHull()
    {
        var hull = With(new ConvexHullCollider(), Shape(new Vector3F(-1), new Vector3F(1)));

        hull.Position = new Vector3F(0, 4, 0);
        hull.FollowMesh(Shape(new Vector3F(-2), new Vector3F(2)).GetComponent<MeshData>().Mesh);

        Assert.Multiple(() =>
        {
            Assert.That(hull.IsFitted, Is.False);
            Assert.That(hull.Hull.GetBoundingBox().Maximum.Y, Is.EqualTo(2).Within(1e-5f), "the new mesh's hull");
            Assert.That(hull.PlacedHull.GetBoundingBox().Maximum.Y, Is.EqualTo(6).Within(1e-5f), "moved by hand");
            Assert.That(hull.Bounds.Center.Y, Is.EqualTo(4).Within(1e-5));
        });
    }

    [Test]
    public void ACopy_IsFittedAsTheOriginal_WithAnOutlineOfItsOwn()
    {
        var fitted = With(new BoxCollider(), Shape(new Vector3F(1), new Vector3F(3)));
        var sized = With(new CapsuleCollider { Radius = 2 }, Shape(new Vector3F(-1), new Vector3F(1)));
        fitted.GetVisualRepresentation();

        var fittedCopy = (BoxCollider)fitted.Clone();
        fitted.CloneValues(fittedCopy);
        var sizedCopy = (CapsuleCollider)sized.Clone();
        sized.CloneValues(sizedCopy);

        Assert.Multiple(() =>
        {
            Assert.That(fittedCopy.IsFitted, Is.True, "a mesh off its pivot leaves the copy fitted");
            Assert.That(fittedCopy.Position, Is.EqualTo(new Vector3F(2)));
            Assert.That(fittedCopy.Bounds, Is.EqualTo(fitted.Bounds));
            Assert.That(fittedCopy.Geometry, Is.Null, "the original's outline is not shared");
            Assert.That(sizedCopy.IsFitted, Is.False);
            Assert.That(sizedCopy.Radius, Is.EqualTo(2));
        });
    }

    [Test]
    public void AMerge_FitsTheBoxRoundBoth_Unscaled()
    {
        var root = Shape(new Vector3F(-1), new Vector3F(1));
        var box = With(new BoxCollider(), root);
        box.Scale = new Vector3F(2);
        var part = Shape(new Vector3F(-1), new Vector3F(1), root);
        part.Transform.Position = new Vector3<double>(5, 0, 0);
        var partBox = With(new BoxCollider(), part);

        box.Merge(partBox);

        Assert.Multiple(() =>
        {
            Assert.That(box.Scale, Is.EqualTo(Vector3F.One));
            Assert.That(box.Bounds.Size.X, Is.EqualTo(8).Within(1e-4), "from the scaled box's side to the part's");
        });
    }

    [Test]
    public void AHullsScale_StretchesItAboutItsCenter()
    {
        var hull = With(new ConvexHullCollider(), Shape(new Vector3F(9, -1, -1), new Vector3F(11, 1, 1)));
        var shared = hull.PlacedHull;

        hull.Scale = new Vector3F(2);

        Assert.Multiple(() =>
        {
            Assert.That(shared, Is.SameAs(hull.Hull), "unplaced, the mesh's own hull");
            Assert.That(hull.Bounds.Center.X, Is.EqualTo(10).Within(1e-4));
            Assert.That(hull.Bounds.Size.X, Is.EqualTo(4).Within(1e-4));
            Assert.That(hull.IsFitted, Is.True);
        });
    }

    private static T With<T>(T collider, Entity entity) where T : Collider
    {
        entity.AddComponent(collider);
        collider.Initialize();
        return collider;
    }

    private static Entity Shape(Vector3F minimum, Vector3F maximum, Entity owner = null)
    {
        var corners = new Vector3F[OrientedBoundingBox<float>.CornerCount];
        new BoundingBox<float>(minimum, maximum).GetCorners(corners);
        var points = new Vector3<double>[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            points[i] = corners[i].As<double>();
        }

        var entity = new Entity(owner, "Shape");
        entity.AddComponent(new MeshData { Mesh = new Mesh(PrimitiveType.TriangleList).SetPoints(points) });
        return entity;
    }
}
