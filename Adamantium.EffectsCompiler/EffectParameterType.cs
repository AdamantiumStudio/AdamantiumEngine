namespace Adamantium.EffectsCompiler;

/// <summary>What an effect parameter is: the scalar type of a value, or the kind of descriptor a resource is bound by,
/// as Vulkan names it.</summary>
public enum EffectParameterType : byte
{
    /// <summary>Nothing, or a structure.</summary>
    Void = 0,

    Bool,

    Int8,

    UInt8,

    Int16,

    UInt16,

    Int,

    UInt,

    Int64,

    UInt64,

    Float16,

    Float,

    Double,

    /// <summary>A uniform buffer: <c>ConstantBuffer&lt;T&gt;</c> or the buffer of loose uniforms.</summary>
    UniformBuffer,

    /// <summary>A storage buffer: <c>StructuredBuffer</c>, <c>ByteAddressBuffer</c> and their <c>RW</c> forms.</summary>
    StorageBuffer,

    /// <summary>A uniform texel buffer: <c>Buffer&lt;T&gt;</c>.</summary>
    UniformTexelBuffer,

    /// <summary>A storage texel buffer: <c>RWBuffer&lt;T&gt;</c>.</summary>
    StorageTexelBuffer,

    /// <summary>A sampled image of one dimension.</summary>
    SampledImage1D,

    /// <summary>A sampled image of two dimensions.</summary>
    SampledImage2D,

    /// <summary>A sampled image of three dimensions.</summary>
    SampledImage3D,

    /// <summary>A sampled cube image.</summary>
    SampledImageCube,

    /// <summary>A storage image of one dimension.</summary>
    StorageImage1D,

    /// <summary>A storage image of two dimensions.</summary>
    StorageImage2D,

    /// <summary>A storage image of three dimensions.</summary>
    StorageImage3D,

    /// <summary>A sampler.</summary>
    Sampler,

    /// <summary>A ray-tracing acceleration structure.</summary>
    AccelerationStructure
}
