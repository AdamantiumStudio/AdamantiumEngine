using System.Numerics;
using Adamantium.Mathematics.Typed;

namespace Adamantium.MathTests.Golden;

/// <summary>A recorded call's inputs read in order as the new types.</summary>
public sealed class GoldenInputs
{
    private readonly double[] numbers;
    private int next;

    /// <summary>Reads <paramref name="numbers"/> from the start.</summary>
    public GoldenInputs(double[] numbers)
    {
        this.numbers = numbers;
    }

    /// <summary>The next number as <typeparamref name="T"/>.</summary>
    public T Number<T>() where T : INumber<T> => T.CreateTruncating(numbers[next++]);

    /// <summary>The next number as an int.</summary>
    public int Int() => (int)numbers[next++];

    /// <summary>The next two numbers as a vector.</summary>
    public Vector2<T> Vector2<T>() where T : unmanaged, INumber<T> => new(Number<T>(), Number<T>());

    /// <summary>The next three numbers as a vector.</summary>
    public Vector3<T> Vector3<T>() where T : unmanaged, INumber<T> => new(Number<T>(), Number<T>(), Number<T>());

    /// <summary>The next four numbers as a vector.</summary>
    public Vector4<T> Vector4<T>() where T : unmanaged, INumber<T> => new(Number<T>(), Number<T>(), Number<T>(), Number<T>());

    /// <summary>The next six numbers as a matrix, row by row.</summary>
    public Matrix3x2<T> Matrix3x2<T>() where T : unmanaged, IFloatingPointIeee754<T> => new(Vector2<T>(), Vector2<T>(), Vector2<T>());

    /// <summary>The next nine numbers as a matrix, row by row.</summary>
    public Matrix3x3<T> Matrix3x3<T>() where T : unmanaged, IFloatingPointIeee754<T> => new(Vector3<T>(), Vector3<T>(), Vector3<T>());

    /// <summary>The next sixteen numbers as a matrix, row by row.</summary>
    public Matrix4x4<T> Matrix4x4<T>() where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Vector4<T>(), Vector4<T>(), Vector4<T>(), Vector4<T>());

    /// <summary>The next two numbers as a width and a height.</summary>
    public Size<T> Size<T>() where T : unmanaged, INumber<T> => new(Number<T>(), Number<T>());

    /// <summary>The next four numbers as a rectangle's X, Y, Width and Height, as the integer rectangle held them.</summary>
    public Rectangle<T> Rectangle<T>() where T : unmanaged, INumber<T> => new(Number<T>(), Number<T>(), Number<T>(), Number<T>());

    /// <summary>The next four numbers as a rectangle's left, top, right and bottom, as the float rectangle held them.</summary>
    public Rectangle<T> RectangleByCorners<T>() where T : unmanaged, INumber<T> =>
        Adamantium.Mathematics.Typed.Rectangle.CreateFromCorners(Number<T>(), Number<T>(), Number<T>(), Number<T>());

    /// <summary>The next four numbers as a thickness: left, top, right, bottom.</summary>
    public Adamantium.Mathematics.Thickness Thickness() =>
        new(Number<double>(), Number<double>(), Number<double>(), Number<double>());

    /// <summary>The next six numbers as a ray: its start, then its direction.</summary>
    public Ray<T> Ray<T>() where T : unmanaged, IFloatingPointIeee754<T> => new(Vector3<T>(), Vector3<T>());

    /// <summary>The next four numbers as a plane: its normal, then D.</summary>
    public Plane<T> Plane<T>() where T : unmanaged, IFloatingPointIeee754<T> => new(Vector3<T>(), Number<T>());

    /// <summary>The next six numbers as a box: its minimum, then its maximum.</summary>
    public BoundingBox<T> BoundingBox<T>() where T : unmanaged, IFloatingPointIeee754<T> => new(Vector3<T>(), Vector3<T>());

    /// <summary>The next four numbers as a sphere: its center, then its radius.</summary>
    public BoundingSphere<T> BoundingSphere<T>() where T : unmanaged, IFloatingPointIeee754<T> => new(Vector3<T>(), Number<T>());

    /// <summary>The next six numbers as a viewport: X, Y, width, height, then the depth range.</summary>
    public Viewport<T> Viewport<T>() where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Number<T>(), Number<T>(), Number<T>(), Number<T>(), Number<T>(), Number<T>());

    /// <summary>The next four numbers as a quaternion.</summary>
    public Quaternion<T> Quaternion<T>() where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Number<T>(), Number<T>(), Number<T>(), Number<T>());
}
