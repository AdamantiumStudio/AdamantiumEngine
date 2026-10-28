using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A ray of fractional numbers: a start and a direction. Distances along it are in units of the direction - of length
/// one, they are lengths. Equality is exact; <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Ray<T> : IEquatable<Ray<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Where the ray starts.</summary>
    public Vector3<T> Position;

    /// <summary>Which way it runs.</summary>
    public Vector3<T> Direction;

    /// <summary>A ray from <paramref name="position"/> along <paramref name="direction"/>.</summary>
    public Ray(Vector3<T> position, Vector3<T> direction)
    {
        Position = position;
        Direction = direction;
    }

    /// <summary>Whether the ray passes through <paramref name="point"/>, as
    /// <see cref="Collision.RayIntersectsPoint{T}"/> decides.</summary>
    public readonly bool Intersects(Vector3<T> point) => Collision.RayIntersectsPoint(this, point);

    /// <summary>Whether the ray meets <paramref name="ray"/>, and where.</summary>
    public readonly bool Intersects(Ray<T> ray, out Vector3<T> point) => Collision.RayIntersectsRay(this, ray, out point);

    /// <summary>Whether the ray meets <paramref name="plane"/> ahead of its start, and how far along.</summary>
    public readonly bool Intersects(Plane<T> plane, out T distance) => Collision.RayIntersectsPlane(this, plane, out distance);

    /// <summary>Whether the ray meets <paramref name="plane"/> ahead of its start, and where.</summary>
    public readonly bool Intersects(Plane<T> plane, out Vector3<T> point) => Collision.RayIntersectsPlane(this, plane, out point);

    /// <summary>Whether the ray comes within <paramref name="limitDistance"/> of the segment from
    /// <paramref name="lineStart"/> to <paramref name="lineEnd"/>, how near it comes, and the segment's point there.</summary>
    public readonly bool Intersects(Vector3<T> lineStart, Vector3<T> lineEnd, T limitDistance, out T distance, out Vector3<T> point)
    {
        distance = Collision.RayIntersectsLineSegment(this, lineStart, lineEnd, out point);
        return distance <= limitDistance;
    }

    /// <summary>Whether the ray meets the triangle, and how far along.</summary>
    public readonly bool Intersects(Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3, out T distance) =>
        Collision.RayIntersectsTriangle(this, vertex1, vertex2, vertex3, out distance);

    /// <summary>Whether the ray meets the triangle, and where.</summary>
    public readonly bool Intersects(Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3, out Vector3<T> point) =>
        Collision.RayIntersectsTriangle(this, vertex1, vertex2, vertex3, out point);

    /// <summary>Whether the ray meets <paramref name="box"/>.</summary>
    public readonly bool Intersects(BoundingBox<T> box) => Collision.RayIntersectsBox(this, box, out T _);

    /// <summary>Whether the ray meets <paramref name="box"/>, and how far along - zero from inside.</summary>
    public readonly bool Intersects(BoundingBox<T> box, out T distance) => Collision.RayIntersectsBox(this, box, out distance);

    /// <summary>Whether the ray meets <paramref name="box"/>, and where.</summary>
    public readonly bool Intersects(BoundingBox<T> box, out Vector3<T> point) => Collision.RayIntersectsBox(this, box, out point);

    /// <summary>Whether the ray meets <paramref name="sphere"/>.</summary>
    public readonly bool Intersects(BoundingSphere<T> sphere) => Collision.RayIntersectsSphere(this, sphere, out T _);

    /// <summary>Whether the ray meets <paramref name="sphere"/>, and how far along - zero from inside.</summary>
    public readonly bool Intersects(BoundingSphere<T> sphere, out T distance) => Collision.RayIntersectsSphere(this, sphere, out distance);

    /// <summary>Whether the ray meets <paramref name="sphere"/>, and where.</summary>
    public readonly bool Intersects(BoundingSphere<T> sphere, out Vector3<T> point) => Collision.RayIntersectsSphere(this, sphere, out point);

    /// <summary>The ray in another number type, each value converted as a cast would.</summary>
    public readonly Ray<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Position.As<TOther>(), Direction.As<TOther>());

    /// <summary>Whether the start and the direction of the two are within
    /// <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Ray<T> left, Ray<T> right) =>
        Vector3<T>.NearEqual(left.Position, right.Position) && Vector3<T>.NearEqual(left.Direction, right.Direction);

    /// <summary>Whether the starts and the directions are equal; NaN equals nothing.</summary>
    public static bool operator ==(Ray<T> left, Ray<T> right) => left.Position == right.Position && left.Direction == right.Direction;

    /// <summary>Whether the starts or the directions differ; NaN differs from everything.</summary>
    public static bool operator !=(Ray<T> left, Ray<T> right) => !(left == right);

    /// <summary>Whether the starts and the directions are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(Ray<T> other) => Position.Equals(other.Position) && Direction.Equals(other.Direction);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Ray<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Position, Direction);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Position:{0} Direction:{1}", Position.ToString(format, formatProvider),
            Direction.ToString(format, formatProvider));
}
