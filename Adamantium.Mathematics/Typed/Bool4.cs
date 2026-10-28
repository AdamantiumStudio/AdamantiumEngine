using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// Four truths kept as four 32-bit numbers, 1 for true and 0 for false - a vertex value of the format R32G32B32A32 UINT.
/// Equality goes by the truths: any number but 0 is true.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Bool4 : IEquatable<Bool4>, IFormattable
{
    private uint x;
    private uint y;
    private uint z;
    private uint w;

    /// <summary>The four truths <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>,
    /// <paramref name="w"/>.</summary>
    public Bool4(bool x, bool y, bool z, bool w)
    {
        this.x = x ? 1u : 0u;
        this.y = y ? 1u : 0u;
        this.z = z ? 1u : 0u;
        this.w = w ? 1u : 0u;
    }

    /// <summary>The first truth.</summary>
    public bool X { readonly get => x != 0; set => x = value ? 1u : 0u; }

    /// <summary>The second truth.</summary>
    public bool Y { readonly get => y != 0; set => y = value ? 1u : 0u; }

    /// <summary>The third truth.</summary>
    public bool Z { readonly get => z != 0; set => z = value ? 1u : 0u; }

    /// <summary>The fourth truth.</summary>
    public bool W { readonly get => w != 0; set => w = value ? 1u : 0u; }

    /// <summary>Whether the truths are the same.</summary>
    public static bool operator ==(Bool4 left, Bool4 right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>Whether a truth differs.</summary>
    public static bool operator !=(Bool4 left, Bool4 right) => !(left == right);

    /// <summary>Whether the truths are the same.</summary>
    public readonly bool Equals(Bool4 other) => this == other;

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Bool4 other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => (X ? 1 : 0) | (Y ? 2 : 0) | (Z ? 4 : 0) | (W ? 8 : 0);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "X:{0} Y:{1} Z:{2} W:{3}", X, Y, Z, W);
}
