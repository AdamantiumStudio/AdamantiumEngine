using System;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics;

/// <summary>The consumer's side of a <see cref="SharedHandleType.HostMemory"/> ring: maps its memory, finds the newest
/// frame the producer finished, and gives slots back by writing the frame read from them.</summary>
public sealed unsafe class HostFrameReader : IDisposable
{
    private readonly SharedFrameMemory _memory;
    private readonly SharedSurfaceDescriptor[] _slots;
    private ulong _latest;

    /// <summary>Maps the ring <paramref name="descriptor"/> names, with <see cref="SharedSurfaceDescriptor.Others"/>.</summary>
    public HostFrameReader(SharedSurfaceDescriptor descriptor)
    {
        if (descriptor.HandleType != SharedHandleType.HostMemory)
        {
            throw new ArgumentException("The ring is not in memory of the process.", nameof(descriptor));
        }

        _slots = new SharedSurfaceDescriptor[1 + descriptor.Others.Length];
        _slots[0] = descriptor;
        descriptor.Others.CopyTo(_slots, 1);
        _memory = SharedFrameMemory.Open(descriptor.MemoryPath, descriptor.MemoryOffset, descriptor.AllocationSize,
            (uint)_slots.Length);
        Width = descriptor.Width;
        Height = descriptor.Height;
    }

    public uint Width { get; }

    public uint Height { get; }

    internal int SlotCount => _slots.Length;

    internal ulong SlotSize => _slots[0].AllocationSize;

    internal nuint SlotAddress(int slot) => (nuint)_memory.At(_slots[slot].MemoryOffset);

    /// <summary>The newest frame finished since the last one taken, in <paramref name="slot"/>; false when none is new.
    /// Its pixels stay the producer's to overwrite only once <see cref="GiveBack"/> says it was read.</summary>
    public bool TryTakeNewest(out int slot, out ulong frame)
    {
        slot = -1;
        frame = _latest;
        for (var i = 0; i < _slots.Length; i++)
        {
            var finished = _memory.ReadCounter(_slots[i].CounterOffset);
            if (finished > frame)
            {
                frame = finished;
                slot = i;
            }
        }

        if (slot < 0)
        {
            return false;
        }

        _latest = frame;
        return true;
    }

    /// <summary>The pixels of <paramref name="slot"/>, rows packed, while this reader is not disposed.</summary>
    public ReadOnlySpan<byte> Pixels(int slot) =>
        new(_memory.At(_slots[slot].MemoryOffset), (int)((ulong)Width * Height * (ulong)_slots[slot].Format.SizeInBytes));

    /// <summary>Gives every slot holding <paramref name="frame"/> or an older one back to the producer.</summary>
    public void GiveBack(ulong frame)
    {
        foreach (var slot in _slots)
        {
            var finished = _memory.ReadCounter(slot.CounterOffset);
            if (finished <= frame)
            {
                _memory.WriteCounter(slot.CounterOffset + sizeof(ulong), finished);
            }
        }
    }

    public void Dispose() => _memory.Dispose();
}
