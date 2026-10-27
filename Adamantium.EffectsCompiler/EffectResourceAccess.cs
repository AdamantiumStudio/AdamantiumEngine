namespace Adamantium.EffectsCompiler;

/// <summary>What a shader does with a resource. A storage buffer or image is read only when its SPIR-V marks it
/// <c>NonWritable</c> - a <c>StructuredBuffer</c>, a <c>ByteAddressBuffer</c> - and written only when it marks it
/// <c>NonReadable</c>; every other resource is read only.</summary>
public enum EffectResourceAccess : byte
{
    /// <summary>Read and written.</summary>
    ReadWrite = 0,

    /// <summary>Only read.</summary>
    ReadOnly = 1,

    /// <summary>Only written.</summary>
    WriteOnly = 2
}
