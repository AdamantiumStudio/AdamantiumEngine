using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Core;

/// <summary>The rings of frames in memory of the process a main device's surfaces show (see
/// <see cref="SharedHandleType.HostMemory"/>), each opened once and kept while a surface holds it: a ring whose frames
/// change size is not mapped and imported again for each size.</summary>
public sealed class HostFrameRings
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ImportedFrameRing> _open = new();

    /// <summary>The ring <paramref name="descriptor"/> names, opened on <paramref name="device"/> unless a surface holds
    /// it already; each hold is ended by <see cref="ImportedFrameRing.Release"/>.</summary>
    public ImportedFrameRing Hold(IGraphicsDevice device, SharedSurfaceDescriptor descriptor)
    {
        lock (_sync)
        {
            if (!_open.TryGetValue(descriptor.MemoryPath, out var ring))
            {
                ring = Open(device, descriptor);
                _open.Add(ring.Path, ring);
            }

            ring.Holders++;
            return ring;
        }
    }

    internal void Release(ImportedFrameRing ring)
    {
        lock (_sync)
        {
            if (--ring.Holders > 0)
            {
                return;
            }

            _open.Remove(ring.Path);
        }

        ring.Close();
    }

    private ImportedFrameRing Open(IGraphicsDevice device, SharedSurfaceDescriptor descriptor)
    {
        var reader = new HostFrameReader(descriptor);
        try
        {
            return new ImportedFrameRing(this, descriptor.MemoryPath, reader, TryImportSlots(device, reader));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    // The slots as buffers of the device, or null where it takes none of the process's memory.
    private static HostSlotBuffer[] TryImportSlots(IGraphicsDevice device, HostFrameReader reader)
    {
        if (!HostSlotBuffer.CanImport(device) || !IsAligned(device, reader))
        {
            return null;
        }

        var slots = new HostSlotBuffer[reader.SlotCount];
        try
        {
            for (var slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] = HostSlotBuffer.Import(device, reader.SlotAddress(slot), reader.SlotSize,
                    Vulkan.Core.BufferUsageFlagBits.TransferSrcBit);
            }
        }
        catch (Exception exception)
        {
            foreach (var slot in slots)
            {
                slot?.Dispose();
            }

            if (exception is NotSupportedException)
            {
                return null;
            }

            throw;
        }

        return slots;
    }

    private static bool IsAligned(IGraphicsDevice device, HostFrameReader reader)
    {
        var alignment = device.Adapter.HostPointerAlignment;
        if (reader.SlotSize % alignment != 0)
        {
            return false;
        }

        for (var slot = 0; slot < reader.SlotCount; slot++)
        {
            if (reader.SlotAddress(slot) % alignment != 0)
            {
                return false;
            }
        }

        return true;
    }
}
