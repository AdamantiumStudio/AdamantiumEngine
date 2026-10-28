using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A sphere of fractional numbers around <see cref="Center"/>, its surface part of it. Made with
/// <see cref="BoundingSphere"/>'s factories. Equality is exact; <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoundingSphere<T> : IEquatable<BoundingSphere<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The middle.</summary>
    public Vector3<T> Center;

    /// <summary>The radius.</summary>
    public T Radius;

    /// <summary>The sphere of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    public BoundingSphere(Vector3<T> center, T radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>Twice the radius.</summary>
    public readonly T Diameter => Radius + Radius;

    /// <summary>The sphere scaled by <paramref name="scale"/> from the origin, then moved by
    /// <paramref name="translation"/>.</summary>
    public readonly BoundingSphere<T> Transform(T scale, Vector3<T> translation) => new(Center * scale + translation, Radius * scale);

    /// <summary>A sphere holding this one carried by <paramref name="matrix"/>: its center carried, its radius scaled by
    /// the most the matrix stretches any direction - its largest singular value; for a matrix without shear, the longest
    /// of its axes.</summary>
    public readonly BoundingSphere<T> Transform(Matrix4x4<T> matrix) =>
        new(Vector3<T>.TransformCoordinate(Center, matrix), Radius * LargestStretch(matrix.Right, matrix.Up, matrix.Forward));

    private static T LargestStretch(Vector3<T> row1, Vector3<T> row2, Vector3<T> row3)
    {
        var scale = T.Max(MaxMagnitude(row1), T.Max(MaxMagnitude(row2), MaxMagnitude(row3)));
        if (!(scale > T.Zero && T.IsFinite(scale)))
        {
            return scale;
        }

        (row1, row2, row3) = (row1 / scale, row2 / scale, row3 / scale);
        for (var sweep = 0; sweep < 32; sweep++)
        {
            var rotated = Orthogonalize(ref row1, ref row2) | Orthogonalize(ref row1, ref row3) | Orthogonalize(ref row2, ref row3);
            if (!rotated)
            {
                break;
            }
        }

        return scale * T.Max(row1.Length(), T.Max(row2.Length(), row3.Length()));
    }

    private static bool Orthogonalize(ref Vector3<T> row1, ref Vector3<T> row2)
    {
        var (length1, length2, dot) = (row1.LengthSquared(), row2.LengthSquared(), Vector3<T>.Dot(row1, row2));
        if (!(T.Abs(dot) > T.CreateTruncating(4) * (T.BitIncrement(T.One) - T.One) * T.Sqrt(length1 * length2)))
        {
            return false;
        }

        var zeta = (length2 - length1) / (dot + dot);
        var tangent = T.CopySign(T.One, zeta) / (T.Abs(zeta) + T.Hypot(T.One, zeta));
        var cosine = T.One / T.Hypot(T.One, tangent);
        var sine = cosine * tangent;
        (row1, row2) = (cosine * row1 - sine * row2, sine * row1 + cosine * row2);
        return true;
    }

    private static T MaxMagnitude(Vector3<T> value) => T.Max(T.Abs(value.X), T.Max(T.Abs(value.Y), T.Abs(value.Z)));

    /// <summary>The point of the surface farthest along <paramref name="direction"/>; for no direction, the one along
    /// X.</summary>
    public readonly Vector3<T> Support(Vector3<T> direction)
    {
        var length = direction.Length();
        return length > T.Zero ? Center + direction * (Radius / length) : Center + new Vector3<T>(Radius, T.Zero, T.Zero);
    }

    /// <summary>Whether <paramref name="ray"/> meets the sphere.</summary>
    public readonly bool Intersects(Ray<T> ray) => Collision.RayIntersectsSphere(ray, this, out T _);

    /// <summary>Whether <paramref name="ray"/> meets the sphere, and how far along it - zero from inside.</summary>
    public readonly bool Intersects(Ray<T> ray, out T distance) => Collision.RayIntersectsSphere(ray, this, out distance);

    /// <summary>Whether <paramref name="ray"/> meets the sphere, and where.</summary>
    public readonly bool Intersects(Ray<T> ray, out Vector3<T> point) => Collision.RayIntersectsSphere(ray, this, out point);

    /// <summary>Which side of <paramref name="plane"/> the sphere is on, or whether it crosses it.</summary>
    public readonly PlaneIntersectionType Intersects(Plane<T> plane) => Collision.PlaneIntersectsSphere(plane, this);

    /// <summary>Whether the sphere touches or overlaps the triangle.</summary>
    public readonly bool Intersects(Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3) =>
        Collision.SphereIntersectsTriangle(this, vertex1, vertex2, vertex3);

    /// <summary>Whether the sphere and <paramref name="box"/> touch or overlap.</summary>
    public readonly bool Intersects(BoundingBox<T> box) => Collision.BoxIntersectsSphere(box, this);

    /// <summary>Whether the two spheres touch or overlap.</summary>
    public readonly bool Intersects(BoundingSphere<T> sphere) => Collision.SphereIntersectsSphere(this, sphere);

    /// <summary>Whether <paramref name="box"/> and the sphere touch or overlap.</summary>
    public readonly bool Intersects(OrientedBoundingBox<T> box) => box.Intersects(this);

    /// <summary>Whether the sphere holds <paramref name="point"/>, its surface included.</summary>
    public readonly ContainmentType Contains(Vector3<T> point) => Collision.SphereContainsPoint(this, point);

    /// <summary>Whether the sphere holds the triangle wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3) =>
        Collision.SphereContainsTriangle(this, vertex1, vertex2, vertex3);

    /// <summary>Whether the sphere holds <paramref name="box"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(BoundingBox<T> box) => Collision.SphereContainsBox(this, box);

    /// <summary>Whether the sphere holds <paramref name="sphere"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(BoundingSphere<T> sphere) => Collision.SphereContainsSphere(this, sphere);

    /// <summary>Whether this sphere holds <paramref name="box"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(OrientedBoundingBox<T> box) =>
        box.IsEmpty ? ContainmentType.Disjoint : Collision.SphereContainsBox(box.ToLocal(this), box.LocalBox());

    /// <summary>The sphere in another number type, each value converted as a cast would.</summary>
    public readonly BoundingSphere<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Center.As<TOther>(), TOther.CreateTruncating(Radius));

    /// <summary>Whether the centers and the radii of the two are within
    /// <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(BoundingSphere<T> left, BoundingSphere<T> right) =>
        Vector3<T>.NearEqual(left.Center, right.Center) && MathHelper.Scalar.NearEqual(left.Radius, right.Radius);

    /// <summary>Whether the centers and the radii are equal; NaN equals nothing.</summary>
    public static bool operator ==(BoundingSphere<T> left, BoundingSphere<T> right) => left.Center == right.Center && left.Radius == right.Radius;

    /// <summary>Whether the centers or the radii differ; NaN differs from everything.</summary>
    public static bool operator !=(BoundingSphere<T> left, BoundingSphere<T> right) => !(left == right);

    /// <summary>Whether the centers and the radii are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(BoundingSphere<T> other) => Center.Equals(other.Center) && Radius.Equals(other.Radius);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is BoundingSphere<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Center, Radius);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Center:{0} Radius:{1}", Center.ToString(format, formatProvider), Radius.ToString(format, formatProvider));
}
