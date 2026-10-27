namespace Adamantium.EffectsCompiler;

/// <summary>What shape an effect parameter's value has.</summary>
public enum EffectParameterClass : byte
{
    /// <summary>One value.</summary>
    Scalar = 0,

    /// <summary>A vector.</summary>
    Vector = 1,

    /// <summary>A matrix stored row by row.</summary>
    MatrixRows = 2,

    /// <summary>A matrix stored column by column.</summary>
    MatrixColumns = 3,

    /// <summary>A resource: a buffer, an image or a sampler.</summary>
    Object = 4,

    /// <summary>A structure.</summary>
    Struct = 5,

    /// <summary>A pointer - <c>T*</c> in Slang: the 64-bit device address of data of the type it points to.</summary>
    Pointer = 6
}
