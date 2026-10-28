using System;
using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Gaussian elimination over a matrix held row by row, shared by the matrix types.</summary>
internal static class GaussianElimination
{
    /// <summary>Eliminates the <paramref name="rows"/> x <paramref name="columns"/> matrix in
    /// <paramref name="elements"/> column by column, taking the largest element left in each column as the pivot; a
    /// column with nothing but zeros left is passed. No tolerance: any would call some invertible matrix singular. A NaN
    /// is carried through. With
    /// <paramref name="unitLeads"/> each pivot row is divided out to a leading one; with <paramref name="reduced"/> the
    /// rows above a pivot are cleared too, not only those below.</summary>
    public static void Eliminate<T>(Span<T> elements, int rows, int columns, bool unitLeads, bool reduced)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var lead = 0;
        for (var r = 0; r < rows && lead < columns; r++, lead++)
        {
            var pivotRow = FindPivot(elements, rows, columns, r, lead);
            while (elements[pivotRow * columns + lead] == T.Zero)
            {
                lead++;
                if (lead == columns)
                {
                    return;
                }

                pivotRow = FindPivot(elements, rows, columns, r, lead);
            }

            var pivot = elements.Slice(pivotRow * columns, columns);
            var target = elements.Slice(r * columns, columns);
            if (pivotRow != r)
            {
                for (var c = 0; c < columns; c++)
                {
                    (pivot[c], target[c]) = (target[c], pivot[c]);
                }
            }

            if (unitLeads)
            {
                var divisor = target[lead];
                for (var c = 0; c < columns; c++)
                {
                    target[c] /= divisor;
                }

                if (T.IsFinite(divisor))
                {
                    target[lead] = T.One;
                }
            }

            for (var i = reduced ? 0 : r + 1; i < rows; i++)
            {
                if (i == r)
                {
                    continue;
                }

                var row = elements.Slice(i * columns, columns);
                var factor = row[lead] / target[lead];
                if (factor == T.Zero)
                {
                    continue;
                }

                for (var c = 0; c < columns; c++)
                {
                    row[c] -= factor * target[c];
                }

                if (T.IsFinite(factor))
                {
                    row[lead] = T.Zero;
                }
            }
        }
    }

    private static int FindPivot<T>(Span<T> elements, int rows, int columns, int fromRow, int column)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var best = fromRow;
        for (var i = fromRow + 1; i < rows; i++)
        {
            var candidate = elements[i * columns + column];
            if (T.IsNaN(candidate) || T.Abs(candidate) > T.Abs(elements[best * columns + column]))
            {
                best = i;
            }
        }

        return best;
    }
}
