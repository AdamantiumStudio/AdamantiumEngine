using System;
using System.Globalization;

namespace Adamantium.Mathematics;

public static partial class MathHelper
{
    /// <summary>
    /// <paramref name="value"/> rounded to <paramref name="decimals"/> as its shortest decimal form reads: 2.675 to two
    /// decimals is 2.68 - as a reader of 2.675 expects - though the double's exact binary value is a little under and
    /// <see cref="Math.Round(double, int, MidpointRounding)"/> gives 2.67. One too big for a decimal, or not finite, is
    /// rounded by its binary value.
    /// </summary>
    public static double RoundAsWritten(double value, int decimals, MidpointRounding mode = MidpointRounding.ToEven)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 1e27)
        {
            return Math.Round(value, Math.Clamp(decimals, 0, 15), mode);
        }

        Span<char> text = stackalloc char[32];
        value.TryFormat(text, out var length, "R", CultureInfo.InvariantCulture);
        var written = decimal.Parse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
        return (double)Math.Round(written, Math.Clamp(decimals, 0, 28), mode);
    }
}
