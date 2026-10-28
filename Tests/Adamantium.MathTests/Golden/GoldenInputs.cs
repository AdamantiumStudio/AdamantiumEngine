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

    /// <summary>The next four numbers as a quaternion.</summary>
    public Quaternion<T> Quaternion<T>() where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Number<T>(), Number<T>(), Number<T>(), Number<T>());
}
