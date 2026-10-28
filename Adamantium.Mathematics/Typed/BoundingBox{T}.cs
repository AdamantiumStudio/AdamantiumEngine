using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// An axis-aligned box of fractional numbers, from <see cref="Minimum"/> to <see cref="Maximum"/>, its faces part of it.
/// Made with <see cref="BoundingBox"/>'s factories. Equality is exact; <see cref="NearEqual"/> compares within a
/// tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BoundingBox<T> : IEquatable<BoundingBox<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The corner with the least X, Y and Z.</summary>
    public Vector3<T> Minimum;

    /// <summary>The corner with the greatest X, Y and Z.</summary>
    public Vector3<T> Maximum;

    /// <summary>The box from <paramref name="minimum"/> to <paramref name="maximum"/>.</summary>
    public BoundingBox(Vector3<T> minimum, Vector3<T> maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>The middle of the box.</summary>
    public readonly Vector3<T> Center => (Minimum + Maximum) * T.CreateTruncating(0.5);

    /// <summary>The width, height and depth.</summary>
    public readonly Vector3<T> Size => Maximum - Minimum;

    /// <summary>Writes the eight corners into <paramref name="corners"/>: the four of greatest Z - top left, top right,
    /// bottom right, bottom left, Y up - then the same four of least Z.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="corners"/> holds fewer than eight.</exception>
    public readonly void GetCorners(Span<Vector3<T>> corners)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(corners.Length, 8, nameof(corners));
        corners[0] = new Vector3<T>(Minimum.X, Maximum.Y, Maximum.Z);
        corners[1] = new Vector3<T>(Maximum.X, Maximum.Y, Maximum.Z);
        corners[2] = new Vector3<T>(Maximum.X, Minimum.Y, Maximum.Z);
        corners[3] = new Vector3<T>(Minimum.X, Minimum.Y, Maximum.Z);
        corners[4] = new Vector3<T>(Minimum.X, Maximum.Y, Minimum.Z);
        corners[5] = new Vector3<T>(Maximum.X, Maximum.Y, Minimum.Z);
        corners[6] = new Vector3<T>(Maximum.X, Minimum.Y, Minimum.Z);
        corners[7] = new Vector3<T>(Minimum.X, Minimum.Y, Minimum.Z);
    }

    /// <summary>The corner farthest along <paramref name="direction"/>; for a zero component, the greater side.</summary>
    public readonly Vector3<T> Support(Vector3<T> direction) =>
        new(direction.X >= T.Zero ? Maximum.X : Minimum.X, direction.Y >= T.Zero ? Maximum.Y : Minimum.Y,
            direction.Z >= T.Zero ? Maximum.Z : Minimum.Z);

    /// <summary>Whether <paramref name="ray"/> meets the box.</summary>
    public readonly bool Intersects(Ray<T> ray) => Collision.RayIntersectsBox(ray, this, out T _);

    /// <summary>Whether <paramref name="ray"/> meets the box, and how far along it - zero from inside.</summary>
    public readonly bool Intersects(Ray<T> ray, out T distance) => Collision.RayIntersectsBox(ray, this, out distance);

    /// <summary>Whether <paramref name="ray"/> meets the box, and where.</summary>
    public readonly bool Intersects(Ray<T> ray, out Vector3<T> point) => Collision.RayIntersectsBox(ray, this, out point);

    /// <summary>Which side of <paramref name="plane"/> the box is on, or whether it crosses it.</summary>
    public readonly PlaneIntersectionType Intersects(Plane<T> plane) => Collision.PlaneIntersectsBox(plane, this);

    /// <summary>Whether the two boxes touch or overlap.</summary>
    public readonly bool Intersects(BoundingBox<T> box) => Collision.BoxIntersectsBox(this, box);

    /// <summary>Whether the box and <paramref name="sphere"/> touch or overlap.</summary>
    public readonly bool Intersects(BoundingSphere<T> sphere) => Collision.BoxIntersectsSphere(this, sphere);

    /// <summary>Whether <paramref name="box"/> and this box touch or overlap.</summary>
    public readonly bool Intersects(OrientedBoundingBox<T> box) => box.Intersects(this);

    /// <summary>Whether the box holds <paramref name="point"/>, its faces included.</summary>
    public readonly ContainmentType Contains(Vector3<T> point) => Collision.BoxContainsPoint(this, point);

    /// <summary>Whether the box holds <paramref name="box"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(BoundingBox<T> box) => Collision.BoxContainsBox(this, box);

    /// <summary>Whether the box holds <paramref name="sphere"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(BoundingSphere<T> sphere) => Collision.BoxContainsSphere(this, sphere);

    /// <summary>Whether this box holds <paramref name="box"/> wholly, in part or not at all.</summary>
    public readonly ContainmentType Contains(OrientedBoundingBox<T> box) => OrientedBoundingBox.CreateFromBox(this).Contains(box);

    /// <summary>The box in another number type, each value converted as a cast would.</summary>
    public readonly BoundingBox<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Minimum.As<TOther>(), Maximum.As<TOther>());

    /// <summary>Whether the corners of the two are within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(BoundingBox<T> left, BoundingBox<T> right) =>
        Vector3<T>.NearEqual(left.Minimum, right.Minimum) && Vector3<T>.NearEqual(left.Maximum, right.Maximum);

    /// <summary>Whether the corners are equal; NaN equals nothing.</summary>
    public static bool operator ==(BoundingBox<T> left, BoundingBox<T> right) => left.Minimum == right.Minimum && left.Maximum == right.Maximum;

    /// <summary>Whether a corner differs; NaN differs from everything.</summary>
    public static bool operator !=(BoundingBox<T> left, BoundingBox<T> right) => !(left == right);

    /// <summary>Whether the corners are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(BoundingBox<T> other) => Minimum.Equals(other.Minimum) && Maximum.Equals(other.Maximum);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is BoundingBox<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Minimum, Maximum);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Minimum:{0} Maximum:{1}", Minimum.ToString(format, formatProvider),
            Maximum.ToString(format, formatProvider));
}
