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
    /// the most the matrix stretches any direction - for a matrix without shear, the longest of its axes.</summary>
    public readonly BoundingSphere<T> Transform(Matrix4x4<T> matrix) =>
        new(Vector3<T>.TransformCoordinate(Center, matrix), Radius * LargestStretch(matrix.Right, matrix.Up, matrix.Forward));

    private static T LargestStretch(Vector3<T> row1, Vector3<T> row2, Vector3<T> row3)
    {
        var (g11, g22, g33) = (row1.LengthSquared(), row2.LengthSquared(), row3.LengthSquared());
        var (g12, g13, g23) = (Vector3<T>.Dot(row1, row2), Vector3<T>.Dot(row1, row3), Vector3<T>.Dot(row2, row3));
        var longest = T.Max(g11, T.Max(g22, g33));
        var offDiagonal = g12 * g12 + g13 * g13 + g23 * g23;
        if (offDiagonal == T.Zero)
        {
            return T.Sqrt(longest);
        }

        var three = T.CreateTruncating(3);
        var mean = (g11 + g22 + g33) / three;
        var (b11, b22, b33) = (g11 - mean, g22 - mean, g33 - mean);
        var p = T.Sqrt((b11 * b11 + b22 * b22 + b33 * b33 + (offDiagonal + offDiagonal)) / T.CreateTruncating(6));
        var determinant = b11 * (b22 * b33 - g23 * g23) - g12 * (g12 * b33 - g23 * g13) + g13 * (g12 * g23 - b22 * g13);
        var half = T.Clamp(determinant / (p * p * p) / (T.One + T.One), -T.One, T.One);
        var largest = mean + (p + p) * T.Cos(T.Acos(half) / three);
        return T.Sqrt(T.Max(largest, longest));
    }

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

    /// <summary>Whether the sphere holds <paramref name="point"/>, its surface included.</summary>
    public readonly ContainmentType Contains(Vector3<T> point) => Collision.SphereContainsPoint(this, point);

    /// <summary>Whether the sphere holds the triangle wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3) =>
        Collision.SphereContainsTriangle(this, vertex1, vertex2, vertex3);

    /// <summary>Whether the sphere holds <paramref name="box"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(BoundingBox<T> box) => Collision.SphereContainsBox(this, box);

    /// <summary>Whether the sphere holds <paramref name="sphere"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(BoundingSphere<T> sphere) => Collision.SphereContainsSphere(this, sphere);

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
