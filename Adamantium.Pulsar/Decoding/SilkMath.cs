namespace Adamantium.Pulsar.Decoding;

/// <summary>SILK's fixed-point arithmetic, to the bit: products of 32 by 16 bits keeping the high 32, rounding shifts,
/// saturation, and the approximations of log, exp, square root and division the decoder's filters are made of.</summary>
internal static class SilkMath
{
    /// <summary>(a * (short)b) >> 16.</summary>
    public static int MulWB(int a, int b) => (int)((long)a * (short)b >> 16);

    public static int MulAddWB(int sum, int a, int b) => sum + MulWB(a, b);

    /// <summary>(a * b) >> 16, of 32 bits each.</summary>
    public static int MulWW(int a, int b) => (int)((long)a * b >> 16);

    public static int MulAddWW(int sum, int a, int b) => sum + MulWW(a, b);

    public static int MulBB(int a, int b) => (short)a * (short)b;

    public static int MulAddBB(int sum, int a, int b) => sum + (short)a * (short)b;

    /// <summary>(a * b) >> 32.</summary>
    public static int MulHigh(int a, int b) => (int)((long)a * b >> 32);

    public static int ShiftRound(int value, int shift) =>
        shift == 1 ? (value >> 1) + (value & 1) : ((value >> (shift - 1)) + 1) >> 1;

    public static long ShiftRound(long value, int shift) =>
        shift == 1 ? (value >> 1) + (value & 1) : ((value >> (shift - 1)) + 1) >> 1;

    public static short Saturate16(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    public static int AddSaturate32(int a, int b)
    {
        var sum = (long)a + b;
        return (int)Math.Clamp(sum, int.MinValue, int.MaxValue);
    }

    public static int SubtractSaturate32(int a, int b) => (int)Math.Clamp((long)a - b, int.MinValue, int.MaxValue);

    /// <summary>silk_LSHIFT_SAT32.</summary>
    public static int ShiftLeftSaturate(int value, int shift) =>
        Math.Clamp(value, int.MinValue >> shift, int.MaxValue >> shift) << shift;

    public static int LeadingZeros(int value) => value == 0 ? 32 : int.LeadingZeroCount(value);

    /// <summary>silk_CLZ_FRAC: the leading zeros, and the 7 bits after the highest one.</summary>
    public static void LeadingZerosAndFraction(int value, out int zeros, out int fraction)
    {
        zeros = LeadingZeros(value);
        fraction = (int)uint.RotateRight((uint)value, 24 - zeros) & 0x7F;
    }

    public static int SquareRootApproximation(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        LeadingZerosAndFraction(value, out var zeros, out var fraction);
        var root = (zeros & 1) != 0 ? 32768 : 46214;
        root >>= zeros >> 1;
        return MulAddWB(root, root, MulBB(213, fraction));
    }

    /// <summary>silk_DIV32_varQ: a / b in Q of <paramref name="q"/>.</summary>
    public static int Divide(int a, int b, int q)
    {
        var aHeadroom = LeadingZeros(Math.Abs(a)) - 1;
        var aNormal = a << aHeadroom;
        var bHeadroom = LeadingZeros(Math.Abs(b)) - 1;
        var bNormal = b << bHeadroom;
        var bInverse = (int.MaxValue >> 2) / (bNormal >> 16);
        var result = MulWB(aNormal, bInverse);
        aNormal = unchecked(aNormal - (MulHigh(bNormal, result) << 3));
        result = MulAddWB(result, aNormal, bInverse);
        var shift = 29 + aHeadroom - bHeadroom - q;
        return shift < 0 ? ShiftLeftSaturate(result, -shift) : shift < 32 ? result >> shift : 0;
    }

    /// <summary>silk_INVERSE32_varQ: 1 / b in Q of <paramref name="q"/>.</summary>
    public static int Inverse(int b, int q)
    {
        var headroom = LeadingZeros(Math.Abs(b)) - 1;
        var normal = b << headroom;
        var inverse = (int.MaxValue >> 2) / (normal >> 16);
        var result = inverse << 16;
        var error = ((1 << 29) - MulWB(normal, inverse)) << 3;
        result = result + MulWW(error, inverse);
        var shift = 61 - headroom - q;
        return shift <= 0 ? ShiftLeftSaturate(result, -shift) : shift < 32 ? result >> shift : 0;
    }

    /// <summary>silk_log2lin: 2 to the Q7 power.</summary>
    public static int Log2ToLinear(int log)
    {
        if (log < 0)
        {
            return 0;
        }

        var result = 1 << (log >> 7);
        var fraction = log & 0x7F;
        var adjust = MulAddWB(fraction, MulBB(fraction, 128 - fraction), -174);
        return log < 2048 ? result + (result * adjust >> 7) : result + (result >> 7) * adjust;
    }

    /// <summary>silk_RAND.</summary>
    public static int Random(int seed) => unchecked(907633515 + seed * 196314165);
}
