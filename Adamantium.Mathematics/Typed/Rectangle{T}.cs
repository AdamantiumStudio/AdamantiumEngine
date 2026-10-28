using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// An axis-aligned rectangle of any number type - <c>Rectangle&lt;int&gt;</c> for pixels, <c>Rectangle&lt;float&gt;</c>
/// for layout boxes - stored as X, Y, Width, Height; the right and bottom edges are X + Width and Y + Height. Made with
/// <see cref="Rectangle"/>'s factories. Edges count as inside; an intersection needs an area. Equality is exact;
/// <see cref="NearEqual(Rectangle{T}, Rectangle{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Rectangle<T> : IEquatable<Rectangle<T>>, IFormattable where T : unmanaged, INumber<T>
{
    /// <summary>The left edge.</summary>
    public T X;

    /// <summary>The top edge.</summary>
    public T Y;

    /// <summary>The width.</summary>
    public T Width;

    /// <summary>The height.</summary>
    public T Height;

    /// <summary>A rectangle at <paramref name="x"/>, <paramref name="y"/> of <paramref name="width"/> by
    /// <paramref name="height"/>.</summary>
    public Rectangle(T x, T y, T width, T height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>A rectangle at <paramref name="position"/> of <paramref name="size"/>.</summary>
    public Rectangle(Vector2<T> position, Size<T> size) : this(position.X, position.Y, size.Width, size.Height)
    {
    }

    /// <summary>All zero: what <see cref="Intersect"/> gives for rectangles that do not overlap.</summary>
    public static Rectangle<T> Empty => default;

    /// <summary>The left edge, the same as <see cref="X"/>: setting it moves the rectangle and keeps its width.</summary>
    public T Left { readonly get => X; set => X = value; }

    /// <summary>The top edge, the same as <see cref="Y"/>: setting it moves the rectangle and keeps its height.</summary>
    public T Top { readonly get => Y; set => Y = value; }

    /// <summary>The right edge, X + Width: setting it changes the width.</summary>
    public T Right { readonly get => X + Width; set => Width = value - X; }

    /// <summary>The bottom edge, Y + Height: setting it changes the height.</summary>
    public T Bottom { readonly get => Y + Height; set => Height = value - Y; }

    /// <summary>The top left corner.</summary>
    public Vector2<T> Position
    {
        readonly get => new(X, Y);
        set => (X, Y) = (value.X, value.Y);
    }

    /// <summary>The width and the height.</summary>
    public Size<T> Size
    {
        readonly get => new(Width, Height);
        set => (Width, Height) = (value.Width, value.Height);
    }

    /// <summary>The middle: X plus half the width, Y plus half the height, the halves divided in T - whole numbers for
    /// an integer rectangle.</summary>
    public readonly Vector2<T> Center
    {
        get
        {
            var two = T.One + T.One;
            return new Vector2<T>(X + Width / two, Y + Height / two);
        }
    }

    /// <summary>The top left corner.</summary>
    public readonly Vector2<T> TopLeft => new(X, Y);

    /// <summary>The top right corner.</summary>
    public readonly Vector2<T> TopRight => new(Right, Y);

    /// <summary>The bottom left corner.</summary>
    public readonly Vector2<T> BottomLeft => new(X, Bottom);

    /// <summary>The bottom right corner.</summary>
    public readonly Vector2<T> BottomRight => new(Right, Bottom);

    /// <summary>Whether the rectangle has no area: its width or its height is not above zero.</summary>
    public readonly bool IsEmpty => !(Width > T.Zero && Height > T.Zero);

    /// <summary>Moves the rectangle by <paramref name="x"/> and <paramref name="y"/>.</summary>
    public void Offset(T x, T y)
    {
        X += x;
        Y += y;
    }

    /// <summary>Moves the rectangle by <paramref name="amount"/>.</summary>
    public void Offset(Vector2<T> amount) => Offset(amount.X, amount.Y);

    /// <summary>Grows the rectangle by <paramref name="horizontal"/> on the left and the right and by
    /// <paramref name="vertical"/> at the top and the bottom.</summary>
    public void Inflate(T horizontal, T vertical)
    {
        X -= horizontal;
        Y -= vertical;
        Width += horizontal + horizontal;
        Height += vertical + vertical;
    }

    /// <summary>Whether the point <paramref name="x"/>, <paramref name="y"/> is inside or on an edge.</summary>
    public readonly bool Contains(T x, T y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>Whether <paramref name="point"/> is inside or on an edge.</summary>
    public readonly bool Contains(Vector2<T> point) => Contains(point.X, point.Y);

    /// <summary>Whether <paramref name="value"/> lies wholly inside, its edges allowed on these.</summary>
    public readonly bool Contains(Rectangle<T> value) =>
        X <= value.X && value.Right <= Right && Y <= value.Y && value.Bottom <= Bottom;

    /// <summary>Whether the two overlap in an area - touching edges do not count, as for <see cref="Intersect"/>.</summary>
    public readonly bool Intersects(Rectangle<T> value) =>
        value.X < Right && X < value.Right && value.Y < Bottom && Y < value.Bottom;

    /// <summary>The rectangle in another number type, each value converted as a cast would.</summary>
    public readonly Rectangle<TOther> As<TOther>() where TOther : unmanaged, INumber<TOther> =>
        new(TOther.CreateTruncating(X), TOther.CreateTruncating(Y), TOther.CreateTruncating(Width), TOther.CreateTruncating(Height));

    /// <summary>The overlap of the two, or <see cref="Empty"/> when they overlap in no area.</summary>
    public static Rectangle<T> Intersect(Rectangle<T> value1, Rectangle<T> value2)
    {
        var left = T.Max(value1.X, value2.X);
        var top = T.Max(value1.Y, value2.Y);
        var right = T.Min(value1.Right, value2.Right);
        var bottom = T.Min(value1.Bottom, value2.Bottom);
        return right > left && bottom > top ? new Rectangle<T>(left, top, right - left, bottom - top) : Empty;
    }

    /// <summary>The smallest rectangle holding both.</summary>
    public static Rectangle<T> Union(Rectangle<T> value1, Rectangle<T> value2)
    {
        var left = T.Min(value1.X, value2.X);
        var top = T.Min(value1.Y, value2.Y);
        return new Rectangle<T>(left, top, T.Max(value1.Right, value2.Right) - left, T.Max(value1.Bottom, value2.Bottom) - top);
    }

    /// <summary>Whether each value of the two is within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Rectangle<T> left, Rectangle<T> right) =>
        MathHelper.Scalar.NearEqual(left.X, right.X) && MathHelper.Scalar.NearEqual(left.Y, right.Y)
                                                     && MathHelper.Scalar.NearEqual(left.Width, right.Width)
                                                     && MathHelper.Scalar.NearEqual(left.Height, right.Height);

    /// <summary>Whether the values are equal; NaN equals nothing.</summary>
    public static bool operator ==(Rectangle<T> left, Rectangle<T> right) =>
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;

    /// <summary>Whether a value differs; NaN differs from everything.</summary>
    public static bool operator !=(Rectangle<T> left, Rectangle<T> right) => !(left == right);

    /// <summary>X, Y, Width and Height as X, Y, Z and W.</summary>
    public static explicit operator Vector4<T>(Rectangle<T> value) => new(value.X, value.Y, value.Width, value.Height);

    /// <summary>Whether the values are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Rectangle<T> other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Rectangle<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "X:{0} Y:{1} Width:{2} Height:{3}", X.ToString(format, formatProvider),
            Y.ToString(format, formatProvider), Width.ToString(format, formatProvider), Height.ToString(format, formatProvider));
}
