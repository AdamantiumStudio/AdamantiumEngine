using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

/// <summary>The global descriptor heap (VK_EXT_descriptor_heap): one per logical device, owned by
/// <see cref="MainGraphicsDevice"/> and shared by its render devices.</summary>
public interface IDescriptorHeapManager : System.IDisposable
{
    PhysicalDeviceDescriptorHeapPropertiesEXT DeviceHeapProperties { get; }

    /// <summary>Reserves an aligned byte offset in the global resource heap.</summary>
    uint AllocateResourceOffset(uint descriptorSize, uint alignment);

    /// <summary>Reserves an aligned byte offset in the global sampler heap.</summary>
    uint AllocateSamplerOffset(uint descriptorSize);

    void WriteTexture(uint offset, ITexture texture, DescriptorType type);

    void WriteBuffer(ulong offset, IBuffer buffer, ulong bufferOffset, ulong bufferSize, DescriptorType type);

    void WriteSampler(uint offset, SamplerState samplerState);

    /// <summary>Returns a STABLE heap offset for a texture (bindless): on first use it reserves a slot and writes
    /// the descriptor, then caches and returns that offset. Each unique texture gets its own slot, so binding
    /// texture A then B no longer overwrites a single per-parameter slot (the "last texture wins everywhere" bug).</summary>
    uint GetOrAllocateTextureOffset(ITexture texture, DescriptorType type);

    /// <summary>Where <paramref name="texture"/> is in the resource heap seen as an array of textures: what a shader's
    /// texture array of no length is indexed by. Takes the texture a slot if it has none yet.</summary>
    uint TextureIndex(ITexture texture);

    /// <summary>As <see cref="GetOrAllocateTextureOffset"/>, but for samplers (their own stable slot per sampler).</summary>
    uint GetOrAllocateSamplerOffset(SamplerState samplerState);

    /// <summary>As <see cref="GetOrAllocateTextureOffset"/>, but for a buffer resource (UAV / storage buffer).
    /// Cached by buffer object and assumes whole-buffer binding (offset 0, full size); extend the key to
    /// (buffer, offset, size) if sub-range bindings are ever used.</summary>
    uint GetOrAllocateBufferOffset(IBuffer buffer, DescriptorType type);

    /// <summary>Binds the shared heap onto the calling render device's command buffer.</summary>
    void BindDescriptorHeaps(IGraphicsDevice device);

    /// <summary>Slots holding a stand-in for a parameter nobody bound. Sampling an out-of-heap index returns whatever
    /// descriptor the driver finds there - another effect's live texture - so a shader is handed these instead. Red and
    /// 4x4 in DEBUG so a missing binding is visible; transparent and 1x1 in release.</summary>
    uint FallbackTextureOffset { get; }

    uint FallbackSamplerOffset { get; }
}
