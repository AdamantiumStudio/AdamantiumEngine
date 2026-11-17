using System;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>The physics shape of an entity's collider, scaled as the entity is, and where it stands on the entity.
/// </summary>
internal readonly struct ColliderShape
{
    private ColliderShape(Shape shape, Vector3F offset, QuaternionF turn)
    {
        Shape = shape;
        Offset = offset;
        Turn = turn;
    }

    public Shape Shape { get; }

    /// <summary>Where the shape's center is in the entity's own axes, scale applied.</summary>
    public Vector3F Offset { get; }

    /// <summary>How the shape is turned against the entity.</summary>
    public QuaternionF Turn { get; }

    /// <summary>The shape of <paramref name="collider"/> on an entity scaled by <paramref name="scale"/>; false for a
    /// collider the physics has no shape for yet.</summary>
    public static bool TryCreate(Collider collider, Vector3F scale, out ColliderShape shape)
    {
        var size = Vector3F.Abs(scale);
        switch (collider)
        {
            case BoxCollider box:
            {
                var half = Vector3F.Max(box.Bounds.HalfExtent.As<float>() * size, new Vector3F(Shape.MinimumSize));
                var orientation = box.Bounds.Orientation.As<float>();
                var turn = orientation.LengthSquared() > 0 ? QuaternionF.Normalize(orientation) : QuaternionF.Identity;
                shape = new ColliderShape(new BoxShape(half), box.Bounds.Center.As<float>() * scale, turn);
                return true;
            }
            case SphereCollider sphere:
            {
                var radius = MathF.Max(Vector3F.Max(sphere.Bounds.Size.As<float>()) * 0.5f * Vector3F.Max(size),
                    Shape.MinimumSize);
                shape = new ColliderShape(new DarkMatter.Shapes.SphereShape(radius), sphere.Bounds.Center.As<float>() * scale,
                    QuaternionF.Identity);
                return true;
            }
            case CapsuleCollider capsule:
            {
                var start = capsule.Capsule.Start * scale;
                var end = capsule.Capsule.End * scale;
                var radius = MathF.Max(capsule.Capsule.Radius * Vector3F.Max(size), Shape.MinimumSize);
                var axis = end - start;
                var halfLength = axis.Length() * 0.5f;
                var center = (start + end) * 0.5f;
                shape = halfLength < Shape.MinimumSize
                    ? new ColliderShape(new DarkMatter.Shapes.SphereShape(radius), center, QuaternionF.Identity)
                    : new ColliderShape(new DarkMatter.Shapes.CapsuleShape(halfLength, radius), center,
                        TurnUpTo(axis / (2 * halfLength)));
                return true;
            }
            default:
                shape = default;
                return false;
        }
    }

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
