using System;
using System.Globalization;

namespace Adamantium.Mathematics;

/// <summary>
/// A point of the world: where a body, an entity or a camera stands, kept as closely far from the origin as near it.
/// Two positions differ by a vector; a position moves by one. What it is kept in is its own: double for now. Its meters
/// are set one by one as a field's are typed.
/// </summary>
public struct WorldPosition : IEquatable<WorldPosition>, IFormattable
{
    private Vector3<double> value;

    /// <summary>The point at <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/> meters.</summary>
    public WorldPosition(double x, double y, double z)
    {
        value = new Vector3<double>(x, y, z);
    }

    /// <summary>The point <paramref name="position"/> meters from the origin.</summary>
    public WorldPosition(Vector3<double> position)
    {
        value = position;
    }

    /// <summary>The point <paramref name="position"/> meters from the origin, given in float.</summary>
    public WorldPosition(Vector3<float> position)
    {
        value = position.As<double>();
    }

    /// <summary>The world's origin.</summary>
    public static WorldPosition Zero => default;

    /// <summary>The point written as its meters along X, Y and Z, as <see cref="Vector3{T}.Parse"/> reads them.</summary>
    /// <exception cref="FormatException">The text does not hold three numbers.</exception>
    public static WorldPosition Parse(string value) => new(Vector3<double>.Parse(value));

    /// <summary>Meters along X from the origin.</summary>
    public double X
    {
        readonly get => value.X;
        set => this.value.X = value;
    }

    /// <summary>Meters along Y from the origin.</summary>
    public double Y
    {
        readonly get => value.Y;
        set => this.value.Y = value;
    }

    /// <summary>Meters along Z from the origin.</summary>
    public double Z
    {
        readonly get => value.Z;
        set => this.value.Z = value;
    }

    /// <summary>Whether every coordinate is a number and not endless.</summary>
    public readonly bool IsFinite => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    /// <summary>The point as meters from the origin.</summary>
    public readonly Vector3<double> ToVector() => value;

    /// <summary>The way from <paramref name="origin"/> to this point in float, for a point near it: a frame's own
    /// numbers.</summary>
    public readonly Vector3<float> OffsetFrom(WorldPosition origin) => (value - origin.value).As<float>();

    /// <summary>The point <paramref name="amount"/> of the way from <paramref name="from"/> to
    /// <paramref name="to"/>.</summary>
    public static WorldPosition Lerp(WorldPosition from, WorldPosition to, double amount) =>
        new(from.value + (to.value - from.value) * amount);

    public static WorldPosition operator +(WorldPosition position, Vector3<float> offset) =>
        new(position.value + offset.As<double>());

    public static WorldPosition operator -(WorldPosition position, Vector3<float> offset) =>
        new(position.value - offset.As<double>());

    public static WorldPosition operator +(WorldPosition position, Vector3<double> offset) =>
        new(position.value + offset);

    public static WorldPosition operator -(WorldPosition position, Vector3<double> offset) =>
        new(position.value - offset);

    /// <summary>The way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static Vector3<double> operator -(WorldPosition to, WorldPosition from) => to.value - from.value;

    public static bool operator ==(WorldPosition left, WorldPosition right) => left.value == right.value;

    public static bool operator !=(WorldPosition left, WorldPosition right) => left.value != right.value;

    /// <summary>Whether both stand at the same point, a NaN at a NaN too, as a dictionary's key needs.</summary>
    public readonly bool Equals(WorldPosition other) => value.Equals(other.value);

    public override readonly bool Equals(object obj) => obj is WorldPosition other && Equals(other);

    public override readonly int GetHashCode() => value.GetHashCode();

    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        value.ToString(format, formatProvider);
}
