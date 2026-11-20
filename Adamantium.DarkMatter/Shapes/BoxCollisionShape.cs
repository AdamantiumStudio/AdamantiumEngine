using System;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A box reaching <see cref="HalfExtents"/> from the body's center along its own axes.</summary>
public sealed class BoxCollisionShape : CollisionShape
{
    public BoxCollisionShape(Vector3F halfExtents)
    {
        HalfExtents = new Vector3F(RequireSize(halfExtents.X, nameof(halfExtents)),
            RequireSize(halfExtents.Y, nameof(halfExtents)), RequireSize(halfExtents.Z, nameof(halfExtents)));
        RequireFiniteReach(nameof(halfExtents));
    }

    /// <summary>The box reaching <paramref name="halfX"/>, <paramref name="halfY"/> and <paramref name="halfZ"/> from
    /// the center.</summary>
    public BoxCollisionShape(float halfX, float halfY, float halfZ) : this(new Vector3F(halfX, halfY, halfZ))
    {
    }

    /// <summary>Half the width, height and depth.</summary>
    public Vector3F HalfExtents { get; }

    public override CollisionShapeKind Kind => CollisionShapeKind.Box;

    public override float BoundingRadius => HalfExtents.Length();

    public override MassProperties ComputeMass(float density)
    {
        var size = HalfExtents;
        var mass = density * 8f * size.X * size.Y * size.Z;
        var squared = size * size;
        var inertia = new Vector3F(squared.Y + squared.Z, squared.X + squared.Z, squared.X + squared.Y) * (mass / 3f);
        return new MassProperties(mass, inertia);
    }

    internal override BoundingBox<float> ComputeBounds(in LocalPose pose)
    {
        var axisX = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitX));
        var axisY = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitY));
        var axisZ = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitZ));
        var reach = axisX * HalfExtents.X + axisY * HalfExtents.Y + axisZ * HalfExtents.Z;
        return new BoundingBox<float>(pose.Position - reach, pose.Position + reach);
    }

    public override Vector3F GetSupport(Vector3F direction) =>
        new(direction.X >= 0 ? HalfExtents.X : -HalfExtents.X, direction.Y >= 0 ? HalfExtents.Y : -HalfExtents.Y,
            direction.Z >= 0 ? HalfExtents.Z : -HalfExtents.Z);

    internal override int FlatFaceCount => 6;

    // Faces by axis, then side: +X, -X, +Y, -Y, +Z, -Z.
    internal override Vector3F GetFlatFaceNormal(int face)
    {
        var sign = face % 2 == 0 ? 1f : -1f;
        return (face / 2) switch
        {
            0 => new Vector3F(sign, 0, 0),
            1 => new Vector3F(0, sign, 0),
            _ => new Vector3F(0, 0, sign)
        };
    }

    internal override int GetFlatFace(int face, Span<Vector3F> into) => GetSupportingFace(GetFlatFaceNormal(face), into);

    internal override float GetFlatFaceOffset(int face) => (face / 2) switch
    {
        0 => HalfExtents.X,
        1 => HalfExtents.Y,
        _ => HalfExtents.Z
    };

    internal override int FindFlatFace(Vector3F direction)
    {
        var absolute = Vector3F.Abs(direction);
        if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
            return direction.X >= 0 ? 0 : 1;

        if (absolute.Y >= absolute.Z)
            return direction.Y >= 0 ? 2 : 3;

        return direction.Z >= 0 ? 4 : 5;
    }

    internal override int GetSupportingFace(Vector3F direction, Span<Vector3F> into)
    {
        var size = HalfExtents;
        var absolute = Vector3F.Abs(direction);
        if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
        {
            var x = direction.X >= 0 ? size.X : -size.X;
            into[0] = new Vector3F(x, size.Y, size.Z);
            into[1] = new Vector3F(x, -size.Y, size.Z);
            into[2] = new Vector3F(x, -size.Y, -size.Z);
            into[3] = new Vector3F(x, size.Y, -size.Z);
        }
        else if (absolute.Y >= absolute.Z)
        {
            var y = direction.Y >= 0 ? size.Y : -size.Y;
            into[0] = new Vector3F(size.X, y, size.Z);
            into[1] = new Vector3F(-size.X, y, size.Z);
            into[2] = new Vector3F(-size.X, y, -size.Z);
            into[3] = new Vector3F(size.X, y, -size.Z);
        }
        else
        {
            var z = direction.Z >= 0 ? size.Z : -size.Z;
            into[0] = new Vector3F(size.X, size.Y, z);
            into[1] = new Vector3F(-size.X, size.Y, z);
            into[2] = new Vector3F(-size.X, -size.Y, z);
            into[3] = new Vector3F(size.X, -size.Y, z);
        }

        return 4;
    }

    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        var box = new BoundingBox<float>(-HalfExtents, HalfExtents);
        if (box.Contains(ray.Position) != ContainmentType.Disjoint)
        {
            distance = 0;
            normal = -ray.Direction;
            return true;
        }

        if (!Collision.RayIntersectsBox(ray, box, out distance) || distance > maxDistance)
        {
            normal = default;
            return false;
        }

        var point = ray.Position + ray.Direction * distance;
        var gaps = Vector3F.Abs(Vector3F.Abs(point) - HalfExtents);
        if (gaps.X <= gaps.Y && gaps.X <= gaps.Z)
        {
            normal = new Vector3F(MathF.Sign(point.X), 0, 0);
        }
        else if (gaps.Y <= gaps.Z)
        {
            normal = new Vector3F(0, MathF.Sign(point.Y), 0);
        }
        else
        {
            normal = new Vector3F(0, 0, MathF.Sign(point.Z));
        }

        return true;
    }
}
