using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Effects;

/// <summary>The descriptor heap's linker: every bound resource takes a stable heap slot of its own.</summary>
internal sealed class HeapEffectResourceLinker(IDescriptorHeapManager heap) : EffectResourceLinker
{
    protected override void Bound(ResourceInfo<SamplerState> sampler) =>
        sampler.GlobalHeapOffset = heap.GetOrAllocateSamplerOffset(sampler.Resource);

    protected override void Bound(ResourceInfo<Texture> texture) =>
        texture.GlobalHeapOffset = heap.GetOrAllocateTextureOffset(texture.Resource, DescriptorType.SampledImage);

    protected override void Bound(ResourceInfo<Buffer> buffer) =>
        buffer.GlobalHeapOffset = heap.GetOrAllocateBufferOffset(buffer.Resource, DescriptorType.StorageBuffer);
}
