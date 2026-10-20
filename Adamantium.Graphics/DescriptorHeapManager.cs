using System;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Effects;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

public class DescriptorHeapManager : DisposableObject, IDescriptorHeapManager
{
    private readonly IGraphicsDevice graphicsDevice;
    private ulong currentResourceOffset = 0;
    private ulong currentSamplerOffset = 0;
    // The heap is shared by all render devices of one logical device, which may render concurrently (different
    // universes run in parallel). Serialize the offset allocators and heap writes so the heap path stays correct.
    private readonly object _heapSync = new object();

    public IBuffer ResourceHeapBuffer { get; private set; }
    public IBuffer SamplerHeapBuffer { get; private set; }
    public ulong UsableResourceSpaceSize { get; private set; }
    public ulong UsableSamplerSpaceSize { get; private set; }

    protected Guid Id { get; }

    public PhysicalDeviceDescriptorHeapPropertiesEXT DeviceHeapProperties => graphicsDevice.Adapter.DeviceHeapProperties;

    public DescriptorHeapManager(IGraphicsDevice graphicsDevice)
    {
        Id = Guid.NewGuid();
        this.graphicsDevice = graphicsDevice;
        InitializeHeaps();
    }

    // Binds the shared heap onto the CALLING render device's command buffer (not the heap's construction device).
    public void BindDescriptorHeaps(IGraphicsDevice device)
    {
        ReclaimRetiredSlots();   // once a frame, where a frame demonstrably begins

        IBuffer resourceBuffer = ResourceHeapBuffer;
        IBuffer samplerBuffer = SamplerHeapBuffer;

        ulong resourceReservedSize = device.Adapter.DeviceHeapProperties.MinResourceHeapReservedRange;
        ulong samplerReservedSize = device.Adapter.DeviceHeapProperties.MinSamplerHeapReservedRange;

        var resourceAddressRange = new DeviceAddressRangeKHR();
        resourceAddressRange.Address = resourceBuffer.GetDeviceAddress();
        resourceAddressRange.Size = resourceBuffer.TotalSize;

        var resourceBindInfo = new BindHeapInfoEXT();
        resourceBindInfo.HeapRange = resourceAddressRange;

        resourceBindInfo.ReservedRangeOffset = resourceBuffer.TotalSize - resourceReservedSize;
        resourceBindInfo.ReservedRangeSize = resourceReservedSize;

        var samplerAddressRange = new DeviceAddressRangeKHR();
        samplerAddressRange.Address = samplerBuffer.GetDeviceAddress();
        samplerAddressRange.Size = samplerBuffer.TotalSize;

        var samplerBindInfo = new BindHeapInfoEXT();
        samplerBindInfo.HeapRange = samplerAddressRange;

        samplerBindInfo.ReservedRangeOffset = samplerBuffer.TotalSize - samplerReservedSize;
        samplerBindInfo.ReservedRangeSize = samplerReservedSize;

        device.CurrentCommandBuffer.BindResourceHeapEXT(resourceBindInfo);
        device.CurrentCommandBuffer.BindSamplerHeapEXT(samplerBindInfo);
    }
    
    private void InitializeHeaps()
    {
        var props = graphicsDevice.Adapter.DeviceHeapProperties;

        // IMPORTANT: do not allocate a heap as large as the device maximum. host-visible + device-local memory lives in
        // the small BAR window (~256 MB on NVIDIA Turing) and this is paid per render device, so keep it modest.
        ulong resourceHeapSize = Math.Min((ulong)props.MaxResourceHeapSize, 8UL * 1024 * 1024);
        ulong samplerHeapSize = Math.Min((ulong)props.MaxSamplerHeapSize, 2UL * 1024 * 1024);

        resourceHeapSize = Utilities.AlignSize(resourceHeapSize, props.ResourceHeapAlignment);
        samplerHeapSize = Utilities.AlignSize(samplerHeapSize, props.SamplerHeapAlignment);

        UsableResourceSpaceSize = resourceHeapSize - props.MinResourceHeapReservedRange;
        UsableSamplerSpaceSize = samplerHeapSize - props.MinSamplerHeapReservedRange;

        var flags = BufferUsageFlags.DescriptorHeapExt | BufferUsageFlags.ShaderDeviceAddress;
        // Descriptor heaps are written by the CPU and read by the GPU each frame → CPU-to-GPU upload (BAR window).
        var memFlags = BufferMemoryUsage.UploadFromCpuToGpu;

        ResourceHeapBuffer = Buffer.New(graphicsDevice, resourceHeapSize, flags, memFlags);
        SamplerHeapBuffer = Buffer.New(graphicsDevice, samplerHeapSize, flags, memFlags);
    }

    /// <summary>The two heap buffers are created HERE, so they are destroyed here. Nothing did: the main device nulled
    /// its reference to this manager without disposing it, so every logical device left its heaps behind - which the
    /// validation layer reports at vkDestroyDevice as leaked objects, and which made a second device in one process a
    /// fatal error (every GPU test after the first).</summary>
    protected override void Dispose(bool disposeManagedResources)
    {
        ResourceHeapBuffer?.Dispose();
        ResourceHeapBuffer = null;
        SamplerHeapBuffer?.Dispose();
        SamplerHeapBuffer = null;
        base.Dispose(disposeManagedResources);
    }

    /// <summary>
    /// Allocates an aligned byte offset in the global resource heap.
    /// </summary>
    /// <param name="descriptorSize">Descriptor size.</param>
    /// <param name="alignment">Alignment for the specific descriptor type (BufferDescriptorAlignment or ImageDescriptorAlignment).</param>
    /// <returns>The aligned byte offset to write the descriptor at.</returns>
    public uint AllocateResourceOffset(uint descriptorSize, uint alignment)
    {
        lock (_heapSync)
        {
            ulong alignedOffset = Utilities.AlignSize(currentResourceOffset, alignment);

            if (alignedOffset + descriptorSize > UsableResourceSpaceSize)
            {
                throw new OutOfMemoryException(
                    $"Resource descriptor heap overflow: failed to allocate {descriptorSize} bytes. " +
                    $"Current offset: {alignedOffset}, usable heap limit: {UsableResourceSpaceSize} bytes."
                );
            }

            currentResourceOffset = alignedOffset + descriptorSize;
            return (uint)alignedOffset;
        }
    }
    
    /// <summary>
    /// Allocates an aligned byte offset in the global sampler heap.
    /// </summary>
    public uint AllocateSamplerOffset(uint descriptorSize)
    {
        lock (_heapSync)
        {
            uint alignment = (uint)DeviceHeapProperties.SamplerDescriptorAlignment;
            ulong alignedOffset = Utilities.AlignSize(currentSamplerOffset, alignment);

            if (alignedOffset + descriptorSize > UsableSamplerSpaceSize)
            {
                throw new OutOfMemoryException("Sampler descriptor heap overflow.");
            }

            currentSamplerOffset = alignedOffset + descriptorSize;
            return (uint)alignedOffset;
        }
    }

    // Bindless slot caches: each unique texture/sampler gets ONE stable heap slot (its descriptor written once),
    // instead of one slot per shader parameter that every bind overwrote (which made the last-bound texture/sampler
    // show up on every draw). Keyed by the resource object; render targets keep a stable view, so the slot stays valid.
    // Samplers are descriptions: equal ones share a slot, which lives as long as the heap.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ITexture, uint> _textureHeapOffsets = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SamplerState, uint> _samplerHeapOffsets = new();
    private readonly System.Collections.Generic.Dictionary<SamplerKey, uint> _samplerOffsetsByDescription = new();
    private readonly System.Collections.Generic.Dictionary<IBuffer, uint> _bufferHeapOffsets = new();

    private readonly record struct SamplerKey(
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

    // ---- RETURNING SLOTS ----------------------------------------------------------------------------------------
    // A freed slot goes to a per-kind free list (slots fit one descriptor size) after the frames in flight are done with it.
    private readonly System.Collections.Generic.Queue<uint> _freeImageSlots = new();
    private readonly System.Collections.Generic.Queue<uint> _freeBufferSlots = new();
    private readonly System.Collections.Generic.List<(uint Slot, DescriptorKind Kind, uint Frame)> _retiring = new();

    private enum DescriptorKind { Image, Buffer }

    private void Retire(uint slot, DescriptorKind kind)
    {
        lock (_heapSync) _retiring.Add((slot, kind, graphicsDevice.CurrentFrame));
    }

    /// <summary>Move slots whose frames have gone by into the free lists. Called once per frame, where the heap is bound.
    /// </summary>
    private void ReclaimRetiredSlots()
    {
        var depth = Math.Max(1u, graphicsDevice.MaxFramesInFlight);
        var now = graphicsDevice.CurrentFrame;

        lock (_heapSync)
        {
            for (var i = _retiring.Count - 1; i >= 0; i--)
            {
                var (slot, kind, frame) = _retiring[i];
                // Unsigned wrap is fine: what matters is that a full pipeline's worth of frames has passed.
                if (now - frame < depth) continue;

                (kind == DescriptorKind.Image ? _freeImageSlots : _freeBufferSlots).Enqueue(slot);
                _retiring.RemoveAt(i);
            }
        }
    }

    public uint GetOrAllocateBufferOffset(IBuffer buffer, DescriptorType type)
    {
        lock (_heapSync)
        {
            if (_bufferHeapOffsets.TryGetValue(buffer, out var existing)) return existing;

            uint descSize = (uint)DeviceHeapProperties.BufferDescriptorSize;
            uint descAlignment = (uint)(ulong)DeviceHeapProperties.BufferDescriptorAlignment;
            uint offset = _freeBufferSlots.Count > 0
                ? _freeBufferSlots.Dequeue()
                : AllocateResourceOffset(descSize, descAlignment); // _heapSync is re-entrant
            WriteBuffer(offset, buffer, 0, buffer.TotalSize, type);        // whole-buffer binding
            _bufferHeapOffsets[buffer] = offset;
            Track(buffer as DisposableObject, () => ReleaseBuffer(buffer));
            return offset;
        }
    }

    public uint GetOrAllocateTextureOffset(ITexture texture, DescriptorType type)
    {
        if (_textureHeapOffsets.TryGetValue(texture, out var found)) return found;

        lock (_heapSync)
        {
            if (_textureHeapOffsets.TryGetValue(texture, out var existing)) return existing;

            uint descSize = (uint)DeviceHeapProperties.ImageDescriptorSize;
            uint descAlignment = (uint)DeviceHeapProperties.ImageDescriptorAlignment;
            uint offset = _freeImageSlots.Count > 0
                ? _freeImageSlots.Dequeue()
                : AllocateResourceOffset(descSize, descAlignment); // _heapSync is re-entrant
            WriteTexture(offset, texture, type);
            _textureHeapOffsets[texture] = offset;
            Track(texture as DisposableObject, () => ReleaseTexture(texture));
            return offset;
        }
    }

    // The resource tells us when it dies. Subscribed once, at the moment it takes a slot - which is also the only moment
    // we know it has one.
    private static void Track(DisposableObject resource, Action release)
    {
        if (resource == null) return;
        resource.Disposing += (_, _) => release();
    }

    private void ReleaseTexture(ITexture texture)
    {
        lock (_heapSync)
        {
            if (!_textureHeapOffsets.TryRemove(texture, out var slot)) return;
            Retire(slot, DescriptorKind.Image);
        }
    }

    private void ReleaseBuffer(IBuffer buffer)
    {
        lock (_heapSync)
        {
            if (!_bufferHeapOffsets.Remove(buffer, out var slot)) return;
            Retire(slot, DescriptorKind.Buffer);
        }
    }

    // ---- THE FALLBACK DESCRIPTOR --------------------------------------------------------------------------------
    // Sampled when a parameter was never bound, instead of an out-of-heap index. Red 4x4 in Debug so the miss shows,
    // transparent 1x1 in Release.
    private uint _fallbackTextureOffset = uint.MaxValue;
    private uint _fallbackSamplerOffset = uint.MaxValue;
    private ITexture _fallbackTexture;

    public uint FallbackTextureOffset
    {
        get
        {
            lock (_heapSync)
            {
                if (_fallbackTextureOffset != uint.MaxValue) return _fallbackTextureOffset;

                _fallbackTexture = CreateFallbackTexture();
                _fallbackTextureOffset = GetOrAllocateTextureOffset(_fallbackTexture, DescriptorType.SampledImage);
                return _fallbackTextureOffset;
            }
        }
    }

    public uint FallbackSamplerOffset
    {
        get
        {
            lock (_heapSync)
            {
                if (_fallbackSamplerOffset != uint.MaxValue) return _fallbackSamplerOffset;

                _fallbackSamplerOffset = GetOrAllocateSamplerOffset(SamplerStates.LinearClampToEdge);
                return _fallbackSamplerOffset;
            }
        }
    }

    private ITexture CreateFallbackTexture()
    {
#if DEBUG
        const uint size = 4;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;        // R
            pixels[i + 3] = 255;    // A
        }
#else
        const uint size = 1;
        var pixels = new byte[4];   // transparent black
#endif
        return graphicsDevice.CreateTexture(new TextureDescription
        {
            Width = size,
            Height = size,
            Depth = 1,
            ArrayLayers = 1,
            MipLevels = 1,
            Samples = MSAALevel.None,
            Format = Format.R8G8B8A8_UNORM,
            InitialLayout = ImageLayout.Undefined,
            DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageType = ImageType._2d,
            ImageAspect = ImageAspectFlagBits.ColorBit,
            ImageTiling = ImageTiling.Optimal,
            Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
            Dimension = Imaging.TextureDimension.Texture2D
        }, pixels);
    }

    public uint GetOrAllocateSamplerOffset(SamplerState samplerState)
    {
        if (_samplerHeapOffsets.TryGetValue(samplerState, out var found)) return found;

        lock (_heapSync)
        {
            if (_samplerHeapOffsets.TryGetValue(samplerState, out var existing)) return existing;

            var key = SamplerKey.Of(samplerState.Info);
            if (!_samplerOffsetsByDescription.TryGetValue(key, out var offset))
            {
                offset = AllocateSamplerOffset((uint)DeviceHeapProperties.SamplerDescriptorSize);
                WriteSampler(offset, samplerState);
                _samplerOffsetsByDescription[key] = offset;
            }
            _samplerHeapOffsets[samplerState] = offset;
            return offset;
        }
    }

    public void WriteTexture(uint offset, ITexture texture, DescriptorType type)
    {
        uint descriptorSize = (uint)graphicsDevice.Adapter.DeviceHeapProperties.ImageDescriptorSize;

        var imageInfo = new ImageDescriptorInfoEXT
        {
            PView = texture.Info,
            Layout = texture.ImageLayout
        };

        var resourceInfo = new ResourceDescriptorInfoEXT 
        {
            Type = type
        };
        resourceInfo.Data = new ResourceDescriptorDataEXT();
        resourceInfo.Data.PImage = imageInfo;

        WriteToResourceHeap(offset, descriptorSize, resourceInfo);
    }
    
    public void WriteBuffer(ulong offset, IBuffer buffer, ulong bufferOffset, ulong bufferSize, DescriptorType type)
    {
        uint descriptorSize = (uint)graphicsDevice.Adapter.DeviceHeapProperties.BufferDescriptorSize;

        // Pass the GPU address of this specific object's data start (base + offset within the arena)
        var bufferRange = new DeviceAddressRangeKHR() 
        {
            Address = buffer.GetDeviceAddress() + bufferOffset,
            Size = bufferSize
        };

        var resourceInfo = new ResourceDescriptorInfoEXT
        {
            Type = type
        };
        resourceInfo.Data = new ResourceDescriptorDataEXT();
        resourceInfo.Data.PAddressRange = bufferRange;

        WriteToResourceHeap(offset, descriptorSize, resourceInfo);
    }
    
    public unsafe void WriteSampler(uint offset, SamplerState samplerState)
    {
        uint descriptorSize = (uint)graphicsDevice.Adapter.DeviceHeapProperties.SamplerDescriptorSize;

        var dataPtr = SamplerHeapBuffer.MapMemory();
        void* target = (byte*)dataPtr + offset;
        var range = new HostAddressRangeEXT
        {
            Address = (nuint)target,
            Size = descriptorSize
        };

        graphicsDevice.LogicalDevice.WriteSamplerDescriptorsEXT(1, samplerState.Info, range);
        SamplerHeapBuffer.UnmapMemory();
    }

    private unsafe void WriteToResourceHeap(ulong offset, uint size, ResourceDescriptorInfoEXT info)
    {
        var dataPtr = ResourceHeapBuffer.MapMemory();
        void* target = (byte*)dataPtr + offset;
        var range = new HostAddressRangeEXT
        {
            Address = (nuint)target,
            Size = size
        };

        graphicsDevice.LogicalDevice.WriteResourceDescriptorsEXT(1, info, range);
        ResourceHeapBuffer.UnmapMemory();
    }
}