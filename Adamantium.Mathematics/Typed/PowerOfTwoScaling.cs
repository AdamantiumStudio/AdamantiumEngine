using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Scaling of a square matrix held row by row by powers of two, exact short of overflow and underflow: brings
/// every row and column near one so the products of an inverse stay in range, then takes the scale back out of the
/// inverse.</summary>
internal static class PowerOfTwoScaling
{
    /// <summary>Scales each row of the <paramref name="size"/> x <paramref name="size"/> matrix in
    /// <paramref name="elements"/>, then each column, so its largest finite magnitude is between one and two, writing
    /// the binary exponents taken out into <paramref name="rowExponents"/> and <paramref name="columnExponents"/>; a row
    /// or column of zeros or with a value that is not finite is left as it is, at exponent zero.</summary>
    public static void Equilibrate<T>(Span<T> elements, int size, Span<int> rowExponents, Span<int> columnExponents)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        for (var r = 0; r < size; r++)
        {
            rowExponents[r] = Exponent(elements, r * size, 1, size);
            Scale(elements, r * size, 1, size, -rowExponents[r]);
        }

        for (var c = 0; c < size; c++)
        {
            columnExponents[c] = Exponent(elements, c, size, size);
            Scale(elements, c, size, size, -columnExponents[c]);
        }
    }

    /// <summary>Turns the inverse of a matrix <see cref="Equilibrate{T}"/> scaled into the inverse of the matrix before:
    /// row i is divided by two to column exponent i, column j by two to row exponent j.</summary>
    public static void UnscaleInverse<T>(Span<T> inverse, int size, ReadOnlySpan<int> rowExponents,
        ReadOnlySpan<int> columnExponents) where T : unmanaged, IFloatingPointIeee754<T>
    {
        for (var i = 0; i < size; i++)
        {
            Scale(inverse, i * size, 1, size, -columnExponents[i]);
            Scale(inverse, i, size, size, -rowExponents[i]);
        }
    }

    private static int Exponent<T>(Span<T> elements, int start, int step, int count) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var largest = T.Zero;
        for (var i = 0; i < count; i++)
        {
            var value = elements[start + i * step];
            if (!T.IsFinite(value))
            {
                return 0;
            }

            largest = T.Max(largest, T.Abs(value));
        }

        return largest == T.Zero ? 0 : T.ILogB(largest);
    }

    private static void Scale<T>(Span<T> elements, int start, int step, int count, int exponent)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (exponent == 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            elements[start + i * step] = T.ScaleB(elements[start + i * step], exponent);
        }
    }
}
