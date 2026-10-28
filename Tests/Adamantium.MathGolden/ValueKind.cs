namespace Adamantium.MathGolden;

/// <summary>The edge cases each operation is recorded on besides the random ones.</summary>
public enum ValueKind
{
    /// <summary>Every number zero.</summary>
    Zero,

    /// <summary>Every argument of a type the same random value: equal vectors, parallel rotations.</summary>
    Same,

    /// <summary>The identity where the type has one; otherwise the first number one and the rest zero.</summary>
    Unit,

    /// <summary>Numbers up to 4e30; integers 20.</summary>
    Large,

    /// <summary>Fractional numbers NaN; integers zero.</summary>
    NaN
}
