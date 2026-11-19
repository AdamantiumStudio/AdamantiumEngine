using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>The physics shape of an entity's collider, scaled as the entity is, and where it stands on the entity.
/// </summary>
internal readonly struct ColliderShape
{
    // A skewed box's hull is this cube carried, not hulled anew from its corners.
    private static readonly ConvexHull UnitCube = ConvexHull.FromPoints([
        new Vector3F(-1, -1, -1), new Vector3F(1, -1, -1), new Vector3F(-1, 1, -1), new Vector3F(1, 1, -1),
        new Vector3F(-1, -1, 1), new Vector3F(1, -1, 1), new Vector3F(-1, 1, 1), new Vector3F(1, 1, 1)]);

    private ColliderShape(CollisionShape shape, Vector3F offset, QuaternionF turn)
    {
        Shape = shape;
        Offset = offset;
        Turn = turn;
    }

    public CollisionShape Shape { get; }

    /// <summary>Where the shape's center is in the entity's own axes, scale applied.</summary>
    public Vector3F Offset { get; }

    /// <summary>How the shape is turned against the entity.</summary>
    public QuaternionF Turn { get; }

    /// <summary>The shape of <paramref name="collider"/> on an entity scaled by <paramref name="scale"/>; false for a
    /// collider the physics has no shape for yet. A hull's or a mesh's shape is taken from
    /// <paramref name="carriedShapes"/>, its key put in <paramref name="taken"/>.</summary>
    public static bool TryCreate(Collider collider, Vector3F scale, CarriedShapeCache carriedShapes,
        List<CarriedShapeKey> taken, out ColliderShape shape)
    {
        var size = Vector3F.Abs(scale);
        switch (collider)
        {
            case BoxCollider box:
            {
                var turn = box.Turn;
                var own = Vector3F.Abs(box.Size * box.Scale) * 0.5f;
                if (!Vector3.IsEven(size) && !IsAlongAxes(turn))
                {
                    // Turned on an unevenly scaled entity, a box is skewed: no box fits it, the hull of its corners does.
                    shape = new ColliderShape(SkewedBox(own, turn, scale, carriedShapes, taken), box.Position * scale,
                        QuaternionF.Identity);
                    return true;
                }

                if (scale.X < 0)
                    turn = new QuaternionF(turn.X, -turn.Y, -turn.Z, turn.W);

                var stretch = new Vector3F(
                    (Vector3F.Transform(Vector3F.UnitX, turn) * size).Length(),
                    (Vector3F.Transform(Vector3F.UnitY, turn) * size).Length(),
                    (Vector3F.Transform(Vector3F.UnitZ, turn) * size).Length());
                var half = Vector3F.Max(own * stretch, new Vector3F(CollisionShape.MinimumSize));
                shape = new ColliderShape(new BoxCollisionShape(half), box.Position * scale, turn);
                return true;
            }
            case SphereCollider sphere:
            {
                var radius = MathF.Max(sphere.Sphere.Radius * Vector3F.Max(size), CollisionShape.MinimumSize);
                shape = new ColliderShape(new SphereCollisionShape(radius), sphere.Sphere.Center * scale,
                    QuaternionF.Identity);
                return true;
            }
            case CapsuleCollider capsule:
            {
                var start = capsule.Capsule.Start * scale;
                var end = capsule.Capsule.End * scale;
                var radius = MathF.Max(capsule.Capsule.Radius * Vector3F.Max(size), CollisionShape.MinimumSize);
                var axis = end - start;
                var halfLength = axis.Length() * 0.5f;
                var center = (start + end) * 0.5f;
                shape = halfLength < CollisionShape.MinimumSize
                    ? new ColliderShape(new SphereCollisionShape(radius), center, QuaternionF.Identity)
                    : new ColliderShape(new CapsuleCollisionShape(halfLength, radius), center,
                        TurnUpTo(axis / (2 * halfLength)));
                return true;
            }
            case ConvexHullCollider hullCollider:
            {
                if (hullCollider.PlacedHull is not { } hull)
                    throw new ArgumentException("The hull collider holds no hull: its entity has no mesh.");

                shape = new ColliderShape(
                    carriedShapes.Take(hull, Matrix4x4.CreateScale<float>(scale), CollisionShape.DefaultConvexRadius, taken),
                    Vector3F.Zero, QuaternionF.Identity);
                return true;
            }
            case MeshCollider meshCollider:
            {
                if (meshCollider.Source is not { } mesh)
                    throw new ArgumentException("The mesh collider holds no mesh: its entity has none with points.");

                shape = new ColliderShape(
                    carriedShapes.Take(mesh, meshCollider.Carry * Matrix4x4.CreateScale<float>(scale), taken),
                    Vector3F.Zero, QuaternionF.Identity);
                return true;
            }
            case CylinderCollider cylinder:
            {
                var start = cylinder.Cylinder.Start * scale;
                var end = cylinder.Cylinder.End * scale;
                // A circle stays round only on an even scale: the radius takes the wider of the scales across the axis,
                // the entity's axis nearest the cylinder's left out.
                var along = Vector3F.Abs(cylinder.Cylinder.End - cylinder.Cylinder.Start);
                var across = along.X >= along.Y && along.X >= along.Z ? MathF.Max(size.Y, size.Z)
                    : along.Y >= along.Z ? MathF.Max(size.X, size.Z)
                    : MathF.Max(size.X, size.Y);
                var radius = MathF.Max(cylinder.Cylinder.Radius * across, CollisionShape.MinimumSize);
                var axis = end - start;
                var length = axis.Length();
                var halfHeight = MathF.Max(length * 0.5f, CollisionShape.MinimumSize);
                var turn = length > 0 ? TurnUpTo(axis / length) : QuaternionF.Identity;
                shape = new ColliderShape(new CylinderCollisionShape(halfHeight, radius), (start + end) * 0.5f, turn);
                return true;
            }
            default:
                shape = default;
                return false;
        }
    }

    /// <summary>The shape of <paramref name="collider"/> on an entity skewed in its body, carried there from the entity's
    /// own axes by <paramref name="carry"/>, the entity's place left to the part's pose: a box as the hull of its
    /// corners, a hull or a mesh as it is; false for a round collider, which no hull fits skewed. Its key goes in
    /// <paramref name="taken"/>.</summary>
    public static bool TryCreateSkewed(Collider collider, in Matrix4x4F carry, CarriedShapeCache carriedShapes,
        List<CarriedShapeKey> taken, out CollisionShape shape)
    {
        switch (collider)
        {
            case BoxCollider box:
                // Its own scale in the half sizes, so a flat side is made thick before anything stretches it.
                var standing = Matrix4x4.CreateFromQuaternion<float>(box.Turn) *
                               Matrix4x4.CreateTranslation<float>(box.Position);
                shape = CarriedBox(Vector3F.Abs(box.Size * box.Scale) * 0.5f, standing * carry, carriedShapes, taken);
                return true;
            case ConvexHullCollider hullCollider:
                if (hullCollider.PlacedHull is not { } hull)
                    throw new ArgumentException("The hull collider holds no hull: its entity has no mesh.");

                shape = carriedShapes.Take(hull, carry, CollisionShape.DefaultConvexRadius, taken);
                return true;
            case MeshCollider meshCollider:
                if (meshCollider.Source is not { } mesh)
                    throw new ArgumentException("The mesh collider holds no mesh: its entity has none with points.");

                shape = carriedShapes.Take(mesh, meshCollider.Carry * carry, taken);
                return true;
            default:
                shape = null;
                return false;
        }
    }

    // The box's corners turned, then scaled as the entity is, about the box's center.
    private static ConvexHullCollisionShape SkewedBox(Vector3F half, QuaternionF turn, Vector3F scale,
        CarriedShapeCache carriedShapes, List<CarriedShapeKey> taken) =>
        CarriedBox(half, Matrix4x4.CreateFromQuaternion<float>(turn) * Matrix4x4.CreateScale<float>(scale), carriedShapes,
            taken);

    // A box of half sizes half carried by carry, as the hull of its corners; edges sharp, as a box's are. A side thinner
    // than a thousandth of the box's longest is made that thick, so a flat box stays a solid.
    private static ConvexHullCollisionShape CarriedBox(Vector3F half, in Matrix4x4F carry, CarriedShapeCache carriedShapes,
        List<CarriedShapeKey> taken)
    {
        var thinnest = MathF.Max(CollisionShape.MinimumSize, 1e-3f * Vector3F.Max(half));
        half = Vector3F.Max(half, new Vector3F(thinnest));
        return carriedShapes.Take(UnitCube, Matrix4x4.CreateScale<float>(half) * carry, 0, taken);
    }

    private static bool IsAlongAxes(QuaternionF turn) =>
        Vector3F.Max(Vector3F.Abs(Vector3F.Transform(Vector3F.UnitX, turn))) > 0.9999f
        && Vector3F.Max(Vector3F.Abs(Vector3F.Transform(Vector3F.UnitY, turn))) > 0.9999f;

    private static QuaternionF TurnUpTo(Vector3F direction)
    {
        var cosine = Vector3F.Dot(Vector3F.UnitY, direction);
        var axis = Vector3F.Cross(Vector3F.UnitY, direction);
        var sine = axis.Length();
        if (sine < 1e-6f)
            return cosine > 0 ? QuaternionF.Identity : Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI);

        return Quaternion.CreateFromAxisAngle(axis / sine, MathF.Atan2(sine, cosine));
    }
}
