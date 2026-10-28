using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Scaling of a matrix held row by row by powers of two, exact short of overflow and underflow: brings each row
/// near one so the products of an inverse stay in range, then takes the scale back out.</summary>
internal static class PowerOfTwoScaling
{
    /// <summary>Scales each row of the <paramref name="rows"/> x <paramref name="columns"/> matrix in
    /// <paramref name="elements"/> so its largest finite magnitude is between one and two, writing each row's binary
    /// exponent into <paramref name="exponents"/>; a row of zeros or with a value that is not finite is left as it is,
    /// at exponent zero.</summary>
    public static void ScaleRowsNearOne<T>(Span<T> elements, int rows, int columns, Span<int> exponents)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        for (var r = 0; r < rows; r++)
        {
            var row = elements.Slice(r * columns, columns);
            exponents[r] = Exponent(row);
            if (exponents[r] != 0)
            {
                foreach (ref var value in row)
                {
                    value = T.ScaleB(value, -exponents[r]);
                }
            }
        }
    }

    /// <summary>Divides each column of the matrix in <paramref name="elements"/> by two to the exponent
    /// <see cref="ScaleRowsNearOne{T}"/> gave the row of the same number: the inverse of a matrix whose rows were
    /// scaled, made the inverse of the matrix before.</summary>
    public static void UnscaleInverseColumns<T>(Span<T> elements, int rows, int columns, ReadOnlySpan<int> exponents)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        for (var c = 0; c < columns; c++)
        {
            if (exponents[c] == 0)
            {
                continue;
            }

            for (var r = 0; r < rows; r++)
            {
                elements[r * columns + c] = T.ScaleB(elements[r * columns + c], -exponents[c]);
            }
        }
    }

    private static int Exponent<T>(ReadOnlySpan<T> values) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var largest = T.Zero;
        foreach (var value in values)
        {
            if (!T.IsFinite(value))
            {
                return 0;
            }

            largest = T.Max(largest, T.Abs(value));
        }

        return largest == T.Zero ? 0 : T.ILogB(largest);
    }
}
