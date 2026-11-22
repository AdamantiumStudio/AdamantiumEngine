using System;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Vulkan.Core;
using VulkanBuffer = Adamantium.Vulkan.Core.Buffer;

namespace Adamantium.Graphics.Core;

/// <summary>A slot of <see cref="SharedFrameMemory"/> imported into a device as a buffer (VK_EXT_external_memory_host).
/// </summary>
public sealed class HostSlotBuffer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private DeviceMemory _memory;

    private HostSlotBuffer(IGraphicsDevice device, VulkanBuffer buffer)
    {
        _device = device;
        Buffer = buffer;
    }

    public VulkanBuffer Buffer { get; private set; }

    /// <summary>Whether <paramref name="device"/> imports the memory of the process as its own.</summary>
    public static bool CanImport(IGraphicsDevice device) =>
        device.Adapter.HostPointerAlignment != 0
        && device.MainDevice.IsExtensionEnabled(Constants.VK_EXT_EXTERNAL_MEMORY_HOST_EXTENSION_NAME);

    /// <summary>Imports <paramref name="size"/> bytes at <paramref name="pointer"/>, aligned as the device asks, for
    /// <paramref name="usage"/>; throws <see cref="NotSupportedException"/> when the device takes none of it.</summary>
    public static HostSlotBuffer Import(IGraphicsDevice device, nuint pointer, ulong size, BufferUsageFlagBits usage)
    {
        var slot = new HostSlotBuffer(device, device.LogicalDevice.CreateBuffer(new BufferCreateInfo
        {
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            PNext = new ExternalMemoryBufferCreateInfo { HandleTypes = ExternalMemoryHandleTypeFlagBits.HostAllocationBitExt }
        }));
        try
        {
            slot.Bind(pointer, size);
        }
        catch
        {
            slot.Dispose();
            throw;
        }

        return slot;
    }

    public void Dispose()
    {
        if (Buffer != null)
        {
            _device.Destroy(Buffer);
            Buffer = null;
        }

        if (_memory != null)
        {
            _device.Destroy(_memory);
            _memory = null;
        }
    }

    private void Bind(nuint pointer, ulong size)
    {
        var result = _device.LogicalDevice.GetMemoryHostPointerPropertiesEXT(
            ExternalMemoryHandleTypeFlagBits.HostAllocationBitExt, pointer, out var hostProperties);
        if (result != Result.Success)
        {
            throw new NotSupportedException($"The device takes no memory of the process: {result}.");
        }

        var requirements = _device.LogicalDevice.GetBufferMemoryRequirements(Buffer);
        var typeBits = requirements.MemoryTypeBits & hostProperties.MemoryTypeBits;
        if (!_device.Adapter.TryFindMemoryIndex(typeBits, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out var typeIndex)
            && !_device.Adapter.TryFindMemoryIndex(typeBits, MemoryPropertyFlags.HostVisible, out typeIndex)
            && !_device.Adapter.TryFindMemoryIndex(typeBits, 0, out typeIndex))
        {
            throw new NotSupportedException($"No memory type takes the process's memory (0x{typeBits:X}).");
        }

        result = _device.MainDevice.AllocateMemory(new MemoryAllocateInfo
        {
            AllocationSize = size,
            MemoryTypeIndex = typeIndex,
            PNext = new ImportMemoryHostPointerInfoEXT
            {
                HandleType = ExternalMemoryHandleTypeFlagBits.HostAllocationBitExt,
                PHostPointer = pointer
            }
        }, out var memory);
        if (result != Result.Success)
        {
            throw new NotSupportedException($"The process's memory could not be imported: {result}.");
        }

        _memory = memory;
        result = _device.LogicalDevice.BindBufferMemory(Buffer, memory, 0);
        if (result != Result.Success)
        {
            throw new NotSupportedException($"The imported memory could not be bound: {result}.");
        }
    }
}
