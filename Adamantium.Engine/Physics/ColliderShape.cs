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
    /// collider the physics has no shape for yet. A hull's shape is taken from <paramref name="hullShapes"/>, its key
    /// put in <paramref name="taken"/>.</summary>
    public static bool TryCreate(Collider collider, Vector3F scale, HullShapeCache hullShapes, List<HullShapeKey> taken,
        out ColliderShape shape)
    {
        var size = Vector3F.Abs(scale);
        switch (collider)
        {
            case BoxCollider box:
            {
                var orientation = box.Bounds.Orientation.As<float>();
                var turn = orientation.LengthSquared() > 0 ? QuaternionF.Normalize(orientation) : QuaternionF.Identity;
                if (!IsEven(size) && !IsAlongAxes(turn))
                    throw new ArgumentException("A box turned on an unevenly scaled entity is skewed; no box fits it.");

                if (scale.X < 0)
                    turn = new QuaternionF(turn.X, -turn.Y, -turn.Z, turn.W);

                var stretch = new Vector3F(
                    (Vector3F.Transform(Vector3F.UnitX, turn) * size).Length(),
                    (Vector3F.Transform(Vector3F.UnitY, turn) * size).Length(),
                    (Vector3F.Transform(Vector3F.UnitZ, turn) * size).Length());
                var half = Vector3F.Max(box.Bounds.HalfExtent.As<float>() * stretch, new Vector3F(CollisionShape.MinimumSize));
                shape = new ColliderShape(new BoxCollisionShape(half), box.Bounds.Center.As<float>() * scale, turn);
                return true;
            }
            case SphereCollider sphere:
            {
                var radius = MathF.Max(Vector3F.Max(sphere.Bounds.Size.As<float>()) * 0.5f * Vector3F.Max(size),
                    CollisionShape.MinimumSize);
                shape = new ColliderShape(new SphereCollisionShape(radius), sphere.Bounds.Center.As<float>() * scale,
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
                if (hullCollider.Hull is not { } hull)
                    throw new ArgumentException("The hull collider holds no hull: its entity has no mesh.");

                // A scale read back from a turned entity's matrix differs in its last digits: rounded for the key, copies
                // share the shape the first of them made.
                var rounded = new Vector3F(KeepSixDigits(scale.X), KeepSixDigits(scale.Y), KeepSixDigits(scale.Z));
                shape = new ColliderShape(hullShapes.Take(hull, scale, rounded, taken), Vector3F.Zero,
                    QuaternionF.Identity);
                return true;
            }
            case CylinderCollider cylinder:
            {
                var start = cylinder.Cylinder.Start * scale;
                var end = cylinder.Cylinder.End * scale;
                // A circle stays round only on an even scale: the radius takes the wider of the scales across the axis.
                var across = cylinder.Axis == ColliderAxis.X ? MathF.Max(size.Y, size.Z)
                    : cylinder.Axis == ColliderAxis.Y ? MathF.Max(size.X, size.Z)
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

    private static float KeepSixDigits(float value)
    {
        if (value == 0 || !float.IsFinite(value))
            return value;

        var unit = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) - 5);
        return (float)(Math.Round(value / unit) * unit);
    }

    private static bool IsEven(Vector3F size) =>
        MathF.Abs(size.X - size.Y) <= 1e-5f * size.X && MathF.Abs(size.X - size.Z) <= 1e-5f * size.X;

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
