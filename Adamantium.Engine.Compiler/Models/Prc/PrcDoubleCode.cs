namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>One code of the double table: an exponent, whose mantissa follows the code, or a whole frequent value.</summary>
internal readonly struct PrcDoubleCode
{
    internal PrcDoubleCode(int length, uint code, bool isExponent, int exponent, double value)
    {
        Length = length;
        Code = code;
        IsExponent = isExponent;
        Exponent = exponent;
        Value = value;
    }

    /// <summary>How many bits the code has.</summary>
    internal int Length { get; }

    /// <summary>The code's bits, the first one read the highest.</summary>
    internal uint Code { get; }

    /// <summary>True for an exponent's code, false for a frequent value's.</summary>
    internal bool IsExponent { get; }

    /// <summary>The 11 exponent bits of the IEEE 754 double, for an exponent's code.</summary>
    internal int Exponent { get; }

    /// <summary>The value, for a frequent value's code; its sign follows the code unless it is zero.</summary>
    internal double Value { get; }
}
