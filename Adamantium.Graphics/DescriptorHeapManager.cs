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
    private readonly object _heapSync = new object();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ITexture, uint> _textureHeapOffsets = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SamplerState, uint> _samplerHeapOffsets = new();
    private readonly System.Collections.Generic.Dictionary<SamplerKey, uint> _samplerOffsetsByDescription = new();
    private readonly System.Collections.Generic.Dictionary<IBuffer, uint> _bufferHeapOffsets = new();
    private readonly System.Collections.Generic.Queue<uint> _freeImageSlots = new();
    private readonly System.Collections.Generic.Queue<uint> _freeBufferSlots = new();
    private uint _fallbackTextureOffset = uint.MaxValue;
    private uint _fallbackSamplerOffset = uint.MaxValue;
    private ITexture _fallbackTexture;

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

    /// <summary>Binds the shared heaps onto <paramref name="device"/>'s current command buffer.</summary>
    public void BindTo(IGraphicsDevice device)
    {
        var (resourceBindInfo, samplerBindInfo) = HeapBindInfos(device);
        device.CurrentCommandBuffer.BindResourceHeapEXT(resourceBindInfo);
        device.CurrentCommandBuffer.BindSamplerHeapEXT(samplerBindInfo);
    }

    /// <summary>The heaps as <paramref name="device"/> binds them: what a secondary command buffer inherits.</summary>
    public (BindHeapInfoEXT Resource, BindHeapInfoEXT Sampler) HeapBindInfos(IGraphicsDevice device)
    {
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

        return (resourceBindInfo, samplerBindInfo);
    }

    private void InitializeHeaps()
    {
        var props = graphicsDevice.Adapter.DeviceHeapProperties;

        ulong resourceHeapSize = Math.Min((ulong)props.MaxResourceHeapSize, 8UL * 1024 * 1024);
        ulong samplerHeapSize = Math.Min((ulong)props.MaxSamplerHeapSize, 2UL * 1024 * 1024);

        resourceHeapSize = Utilities.AlignSize(resourceHeapSize, props.ResourceHeapAlignment);
        samplerHeapSize = Utilities.AlignSize(samplerHeapSize, props.SamplerHeapAlignment);

        UsableResourceSpaceSize = resourceHeapSize - props.MinResourceHeapReservedRange;
        UsableSamplerSpaceSize = samplerHeapSize - props.MinSamplerHeapReservedRange;

        var flags = BufferUsageFlags.DescriptorHeapExt | BufferUsageFlags.ShaderDeviceAddress;
        var memFlags = BufferMemoryUsage.UploadFromCpuToGpu;

        ResourceHeapBuffer = Buffer.New(graphicsDevice, resourceHeapSize, flags, memFlags);
        SamplerHeapBuffer = Buffer.New(graphicsDevice, samplerHeapSize, flags, memFlags);
    }

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
            ulong alignedOffset = (currentResourceOffset + alignment - 1) / alignment * alignment;

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

    private enum DescriptorKind { Image, Buffer }

    private void Retire(uint slot, DescriptorKind kind) => graphicsDevice.AddToDeferDisposeQueue(new RetiredSlot(this, slot, kind));

    private sealed class RetiredSlot(DescriptorHeapManager heap, uint slot, DescriptorKind kind) : IDisposable
    {
        public void Dispose()
        {
            lock (heap._heapSync)
            {
                (kind == DescriptorKind.Image ? heap._freeImageSlots : heap._freeBufferSlots).Enqueue(slot);
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
                : AllocateResourceOffset(descSize, descAlignment);
            WriteBuffer(offset, buffer, 0, buffer.TotalSize, type);
            _bufferHeapOffsets[buffer] = offset;
            Track(buffer as DisposableObject, () => ReleaseBuffer(buffer));
            return offset;
        }
    }

    private static uint LeastCommonMultiple(uint a, uint b)
    {
        var (x, y) = (Math.Max(a, 1u), Math.Max(b, 1u));
        var (p, q) = (x, y);
        while (q != 0)
        {
            (p, q) = (q, p % q);
        }

        return x / p * y;
    }

    public uint TextureIndex(ITexture texture)
    {
        return GetOrAllocateTextureOffset(texture, DescriptorType.SampledImage) /
               (uint)DeviceHeapProperties.ImageDescriptorSize;
    }

    public uint GetOrAllocateTextureOffset(ITexture texture, DescriptorType type)
    {
        if (_textureHeapOffsets.TryGetValue(texture, out var found)) return found;

        lock (_heapSync)
        {
            if (_textureHeapOffsets.TryGetValue(texture, out var existing)) return existing;

            uint descSize = (uint)DeviceHeapProperties.ImageDescriptorSize;
            uint descAlignment = LeastCommonMultiple((uint)DeviceHeapProperties.ImageDescriptorAlignment, descSize);
            uint offset = _freeImageSlots.Count > 0
                ? _freeImageSlots.Dequeue()
                : AllocateResourceOffset(descSize, descAlignment);
            WriteTexture(offset, texture, type);
            _textureHeapOffsets[texture] = offset;
            Track(texture as DisposableObject, () => ReleaseTexture(texture));
            return offset;
        }
    }

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

    public uint FallbackTextureOffset
    {
        get
        {
            lock (_heapSync)
            {
                if (_fallbackTextureOffset != uint.MaxValue) return _fallbackTextureOffset;

                _fallbackTexture = FallbackTexture.Create(graphicsDevice);
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
