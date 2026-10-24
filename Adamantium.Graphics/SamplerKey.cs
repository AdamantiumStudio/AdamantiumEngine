using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

// A sampler's description as a value: equal descriptions share one heap slot or one VkSampler.
internal readonly record struct SamplerKey(
    SamplerCreateFlagBits Flags,
    Filter MagFilter,
    Filter MinFilter,
    SamplerMipmapMode MipmapMode,
    SamplerAddressMode AddressModeU,
    SamplerAddressMode AddressModeV,
    SamplerAddressMode AddressModeW,
    float MipLodBias,
    bool AnisotropyEnable,
    float MaxAnisotropy,
    bool CompareEnable,
    CompareOp CompareOp,
    float MinLod,
    float MaxLod,
    BorderColor BorderColor,
    bool UnnormalizedCoordinates,
    object Extension)
{
    public static SamplerKey Of(SamplerCreateInfo info) => new(info.Flags, info.MagFilter, info.MinFilter,
        info.MipmapMode, info.AddressModeU, info.AddressModeV, info.AddressModeW, info.MipLodBias,
        info.AnisotropyEnable, info.MaxAnisotropy, info.CompareEnable, info.CompareOp, info.MinLod, info.MaxLod,
        info.BorderColor, info.UnnormalizedCoordinates, info.PNext);
}
