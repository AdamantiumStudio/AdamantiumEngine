namespace Adamantium.Graphics.Core;

/// <summary>The format of a depth-stencil buffer, by its Vulkan format.</summary>
public enum DepthFormat : uint
{
    /// <summary>No depth-stencil buffer.</summary>
    None = 0,

    /// <summary>16 bits of depth.</summary>
    Depth16 = Adamantium.Vulkan.Core.Format.D16_UNORM,

    /// <summary>16 bits of depth and 8 of stencil.</summary>
    Depth16Stencil8 = Adamantium.Vulkan.Core.Format.D16_UNORM_S8_UINT,

    /// <summary>24 bits of depth and 8 of stencil.</summary>
    Depth24Stencil8 = Adamantium.Vulkan.Core.Format.D24_UNORM_S8_UINT,

    /// <summary>A 32-bit float of depth.</summary>
    Depth32 = Adamantium.Vulkan.Core.Format.D32_SFLOAT,

    /// <summary>A 32-bit float of depth and 8 bits of stencil.</summary>
    Depth32Stencil8X24 = Adamantium.Vulkan.Core.Format.D32_SFLOAT_S8_UINT,
}
