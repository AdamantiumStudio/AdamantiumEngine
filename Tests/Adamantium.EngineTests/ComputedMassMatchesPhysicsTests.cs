using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A body's computed mass is the mass the physics gives the shape it builds from the same collider, scaled the
/// same: the two measure alike.</summary>
[TestFixture]
public class ComputedMassMatchesPhysicsTests
{
    private static readonly Vector3F Stretch = new(2, 1, 0.5f);

    [TestCase("box")]
    [TestCase("turned box")]
    [TestCase("sphere")]
    [TestCase("capsule")]
    [TestCase("cylinder")]
    [TestCase("hull")]
    public void TheComputedMass_IsThePhysicsMass(string kind)
    {
        var entity = new Entity(null, kind);
        entity.Transform.BaseScale = Stretch;
        Collider collider = kind switch
        {
            "box" => new BoxCollider { Size = new Vector3F(1, 2, 3) },
            "turned box" => new BoxCollider
            {
                Size = new Vector3F(1, 2, 3), Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 0.5f)
            },
            "sphere" => new SphereCollider { Radius = 0.7f },
            "capsule" => new CapsuleCollider { Radius = 0.4f, Height = 2 },
            "cylinder" => new CylinderCollider { Radius = 0.5f, Height = 1.5f },
            _ => new ConvexHullCollider()
        };
        if (kind == "hull")
        {
            entity.AddComponent(new MeshData { Mesh = Tetrahedron() });
        }

        var body = new RigidBody { Density = 700 };
        entity.AddComponent(body);
        entity.AddComponent(collider);
        if (kind == "hull")
        {
            collider.Initialize();
        }

        Assert.That(ColliderShape.TryCreate(collider, Stretch, new CarriedShapeCache(), new List<CarriedShapeKey>(),
            out var shape), Is.True);
        var physics = shape.Shape.ComputeMass(700).Mass;

        // A rounded hull is drawn in by its edges' radius: a little lighter in the physics.
        var tolerance = kind == "hull" ? 0.05 : 1e-4;
        Assert.That(body.ComputedMass, Is.EqualTo(physics).Within(physics * tolerance));
    }

    // A box on a part turned under a threefold stretch: the physics weighs the hull of its carried corners, the
    // weighing the box's space stretched by the matrix's determinant.
    [Test]
    public void ACarriedBox_WeighsAsThePhysicsHull()
    {
        var box = new BoxCollider { Size = new Vector3F(1, 2, 3), Scale = new Vector3F(1, 1, 0.5f) };
        var carry = Matrix4x4.CreateFromQuaternion<float>(Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 0.7f)) *
                    Matrix4x4.CreateScale<float>(1, 3, 1);

        Assert.That(ColliderShape.TryCreateCarried(box, carry, new CarriedShapeCache(), new List<CarriedShapeKey>(),
            out var shape), Is.True);
        var physics = shape.ComputeMass(700).Mass;

        Assert.That(700 * box.MeasureCarriedVolume(System.Math.Abs(carry.Determinant())),
            Is.EqualTo(physics).Within(physics * 1e-4));
    }

    private static Mesh Tetrahedron()
    {
        Vector3<double>[] points = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
        return new Mesh(PrimitiveType.TriangleList).SetPoints(points).SetIndices([0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]);
    }
}
