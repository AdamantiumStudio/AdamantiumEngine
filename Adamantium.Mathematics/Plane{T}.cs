using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>
/// A plane of fractional numbers: the points p with Normal·p + D = 0. Its front is the side the normal points to.
/// Made with <see cref="Plane"/>'s factories. Equality is exact; <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Plane<T> : IEquatable<Plane<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The normal: which way the front faces, of length one for distances to come out as lengths.</summary>
    public Vector3<T> Normal;

    /// <summary>The plane's distance from the origin along the normal, negated.</summary>
    public T D;

    /// <summary>A plane of <paramref name="normal"/> and <paramref name="d"/>.</summary>
    public Plane(Vector3<T> normal, T d)
    {
        Normal = normal;
        D = d;
    }

    /// <summary>A plane of normal <paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/> and
    /// <paramref name="d"/>.</summary>
    public Plane(T a, T b, T c, T d) : this(new Vector3<T>(a, b, c), d)
    {
    }

    /// <summary>Scales the plane so its normal is of length one.</summary>
    public void Normalize() => this = Normalize(this);

    /// <summary>Which side <paramref name="point"/> is on, or whether it lies on the plane.</summary>
    public readonly PlaneIntersectionType Intersects(Vector3<T> point) => Collision.PlaneIntersectsPoint(this, point);

    /// <summary>Whether <paramref name="ray"/> meets the plane ahead of its start, and how far along it.</summary>
    public readonly bool Intersects(Ray<T> ray, out T distance) => Collision.RayIntersectsPlane(ray, this, out distance);

    /// <summary>Whether <paramref name="ray"/> meets the plane ahead of its start, and where.</summary>
    public readonly bool Intersects(Ray<T> ray, out Vector3<T> point) => Collision.RayIntersectsPlane(ray, this, out point);

    /// <summary>Whether the two planes meet.</summary>
    public readonly bool Intersects(Plane<T> plane) => Collision.PlaneIntersectsPlane(this, plane);

    /// <summary>Whether the two planes meet, and the line where they do.</summary>
    public readonly bool Intersects(Plane<T> plane, out Ray<T> line) => Collision.PlaneIntersectsPlane(this, plane, out line);

    /// <summary>Which side the triangle is on, or whether it crosses the plane.</summary>
    public readonly PlaneIntersectionType Intersects(Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3) =>
        Collision.PlaneIntersectsTriangle(this, vertex1, vertex2, vertex3);

    /// <summary>Which side <paramref name="box"/> is on, or whether it crosses the plane.</summary>
    public readonly PlaneIntersectionType Intersects(BoundingBox<T> box) => Collision.PlaneIntersectsBox(this, box);

    /// <summary>Which side <paramref name="sphere"/> is on, or whether it crosses the plane.</summary>
    public readonly PlaneIntersectionType Intersects(BoundingSphere<T> sphere) => Collision.PlaneIntersectsSphere(this, sphere);

    /// <summary>Which side <paramref name="box"/> is on, or whether it crosses the plane.</summary>
    public readonly PlaneIntersectionType Intersects(OrientedBoundingBox<T> box) => box.Intersects(this);

    /// <summary>The plane in another number type, each value converted as a cast would.</summary>
    public readonly Plane<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Normal.As<TOther>(), TOther.CreateTruncating(D));

    /// <summary>The plane scaled so its normal is of length one.</summary>
    public static Plane<T> Normalize(Plane<T> plane)
    {
        var inverse = T.One / plane.Normal.Length();
        return new Plane<T>(plane.Normal * inverse, plane.D * inverse);
    }

    /// <summary>The normal and D times <paramref name="scale"/>: the same plane, its distances scaled.</summary>
    public static Plane<T> Multiply(Plane<T> plane, T scale) => plane * scale;

    /// <summary>The plane as A, B, C, D dotted with <paramref name="value"/> as X, Y, Z, W.</summary>
    public static T Dot(Plane<T> plane, Vector4<T> value) => Vector3<T>.Dot(plane.Normal, (Vector3<T>)value) + plane.D * value.W;

    /// <summary>How far in front of the plane the point <paramref name="point"/> lies, in units of the normal's
    /// length.</summary>
    public static T DotCoordinate(Plane<T> plane, Vector3<T> point) => Vector3<T>.Dot(plane.Normal, point) + plane.D;

    /// <summary>The normal dotted with the direction <paramref name="direction"/>.</summary>
    public static T DotNormal(Plane<T> plane, Vector3<T> direction) => Vector3<T>.Dot(plane.Normal, direction);

    /// <summary>The plane turned by <paramref name="rotation"/> around the origin.</summary>
    public static Plane<T> Transform(Plane<T> plane, Quaternion<T> rotation) =>
        new(Vector3<T>.Transform(plane.Normal, rotation), plane.D);

    /// <summary>The plane carried by <paramref name="transformation"/>: the plane that holds the transformed points of
    /// this one.</summary>
    public static Plane<T> Transform(Plane<T> plane, Matrix4x4<T> transformation)
    {
        var inverse = Matrix4x4<T>.Invert(transformation);
        var coefficients = new Vector4<T>(plane.Normal, plane.D);
        return new Plane<T>(Vector4<T>.Dot(coefficients, inverse.Row1), Vector4<T>.Dot(coefficients, inverse.Row2),
            Vector4<T>.Dot(coefficients, inverse.Row3), Vector4<T>.Dot(coefficients, inverse.Row4));
    }

    /// <summary>Whether the normals and the D of the two are within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Plane<T> left, Plane<T> right) =>
        Vector3<T>.NearEqual(left.Normal, right.Normal) && MathHelper.Scalar.NearEqual(left.D, right.D);

    /// <summary>The normal and D times <paramref name="scale"/>.</summary>
    public static Plane<T> operator *(Plane<T> plane, T scale) => new(plane.Normal * scale, plane.D * scale);

    /// <summary>The normal and D times <paramref name="scale"/>.</summary>
    public static Plane<T> operator *(T scale, Plane<T> plane) => plane * scale;

    /// <summary>Whether the normals and the D are equal; NaN equals nothing.</summary>
    public static bool operator ==(Plane<T> left, Plane<T> right) => left.Normal == right.Normal && left.D == right.D;

    /// <summary>Whether the normals or the D differ; NaN differs from everything.</summary>
    public static bool operator !=(Plane<T> left, Plane<T> right) => !(left == right);

    /// <summary>Whether the normals and the D are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(Plane<T> other) => Normal.Equals(other.Normal) && D.Equals(other.D);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Plane<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Normal, D);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "A:{0} B:{1} C:{2} D:{3}", Normal.X.ToString(format, formatProvider),
            Normal.Y.ToString(format, formatProvider), Normal.Z.ToString(format, formatProvider), D.ToString(format, formatProvider));
}
