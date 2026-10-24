using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Effects;

// The descriptor heap's linker: every bound resource takes its own stable heap slot, whose index a pass pushes per draw.
internal class HeapEffectResourceLinker(IDescriptorHeapManager heap) : EffectResourceLinker
{
    protected override uint SlotOf(SamplerState sampler) => heap.GetOrAllocateSamplerOffset(sampler);

    protected override uint SlotOf(Texture texture) =>
        heap.GetOrAllocateTextureOffset(texture, DescriptorType.SampledImage);

    protected override uint SlotOf(Buffer buffer) => heap.GetOrAllocateBufferOffset(buffer, DescriptorType.StorageBuffer);
}
