using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Effects;

/// <summary>The descriptor heap's linker: every bound resource takes a stable heap slot of its own.</summary>
internal class HeapEffectResourceLinker(IDescriptorHeapManager heap) : EffectResourceLinker
{
    protected override uint SlotOf(SamplerState sampler) => heap.GetOrAllocateSamplerOffset(sampler);

    protected override uint SlotOf(Texture texture) =>
        heap.GetOrAllocateTextureOffset(texture, DescriptorType.SampledImage);

    protected override uint SlotOf(Buffer buffer) => heap.GetOrAllocateBufferOffset(buffer, DescriptorType.StorageBuffer);
}
