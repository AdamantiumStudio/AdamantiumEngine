using System;
using System.Globalization;
using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Reads the numbers of a value written as text - in markup or a text file: separated by spaces or commas, in
/// the invariant culture.</summary>
internal static class MarkupNumbers
{
    /// <exception cref="FormatException"><paramref name="value"/> does not hold as many numbers as
    /// <paramref name="numbers"/> takes; <paramref name="shape"/> says how they are written.</exception>
    public static void Parse<T>(string value, Span<T> numbers, string shape) where T : INumber<T>
    {
        ArgumentNullException.ThrowIfNull(value);
        var parts = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != numbers.Length)
        {
            throw new FormatException(
                $"\"{value}\" must hold {numbers.Length} numbers (\"{shape}\"), but holds {parts.Length}.");
        }

        for (var i = 0; i < parts.Length; i++)
        {
            numbers[i] = T.Parse(parts[i], CultureInfo.InvariantCulture);
        }
    }
}
