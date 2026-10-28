using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A ray in the plane, of fractional numbers: a start and a direction. Distances along it are in units of the
/// direction - of length one, they are lengths. Made with <see cref="Ray2"/>'s factory. Equality is exact;
/// <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Ray2<T> : IEquatable<Ray2<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>Where the ray starts.</summary>
    public Vector2<T> Position;

    /// <summary>Which way it runs.</summary>
    public Vector2<T> Direction;

    /// <summary>A ray from <paramref name="position"/> along <paramref name="direction"/>.</summary>
    public Ray2(Vector2<T> position, Vector2<T> direction)
    {
        Position = position;
        Direction = direction;
    }

    /// <summary>The point <paramref name="distance"/> along the ray, in units of its direction.</summary>
    public readonly Vector2<T> PointAt(T distance) => Position + Direction * distance;

    /// <summary>The ray in another number type, each value converted as a cast would.</summary>
    public readonly Ray2<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Position.As<TOther>(), Direction.As<TOther>());

    /// <summary>Whether the start and the direction of the two are within
    /// <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Ray2<T> left, Ray2<T> right) =>
        Vector2<T>.NearEqual(left.Position, right.Position) && Vector2<T>.NearEqual(left.Direction, right.Direction);

    /// <summary>Whether the starts and the directions are equal; NaN equals nothing.</summary>
    public static bool operator ==(Ray2<T> left, Ray2<T> right) => left.Position == right.Position && left.Direction == right.Direction;

    /// <summary>Whether the starts or the directions differ; NaN differs from everything.</summary>
    public static bool operator !=(Ray2<T> left, Ray2<T> right) => !(left == right);

    /// <summary>Whether the starts and the directions are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(Ray2<T> other) => Position.Equals(other.Position) && Direction.Equals(other.Direction);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Ray2<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Position, Direction);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Position:{0} Direction:{1}", Position.ToString(format, formatProvider),
            Direction.ToString(format, formatProvider));
}
