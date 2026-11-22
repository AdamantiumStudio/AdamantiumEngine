using System;

namespace Adamantium.Graphics.Core;

/// <summary>The consumer's side of a <see cref="SharedHandleType.HostMemory"/> ring: maps its memory, finds the newest
/// frame of a size the producer finished, and gives slots back by writing the frame read from them. One for a ring,
/// whatever sizes its frames come in.</summary>
public sealed unsafe class HostFrameReader : IDisposable
{
    private readonly SharedFrameMemory _memory;
    private readonly SharedSurfaceDescriptor[] _slots;

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
    }

    public int SlotCount => _slots.Length;

    public ulong SlotSize => _slots[0].AllocationSize;

    public nuint SlotAddress(int slot) => (nuint)_memory.At(_slots[slot].MemoryOffset);

    /// <summary>The newest frame of <paramref name="width"/> by <paramref name="height"/> finished after
    /// <paramref name="after"/>, in <paramref name="slot"/>; false when none is. Its pixels stay the producer's to
    /// overwrite only once <see cref="GiveBack"/> says it was read; a slot already given back - the producer may be
    /// writing it - is not taken.</summary>
    public bool TryTakeNewest(ulong after, uint width, uint height, out int slot, out ulong frame)
    {
        slot = -1;
        frame = after;
        for (var i = 0; i < _slots.Length; i++)
        {
            var finished = _memory.ReadCounter(_slots[i].CounterOffset);
            var read = _memory.ReadCounter(_slots[i].CounterOffset + sizeof(ulong));
            if (finished > frame && read < finished
                && _memory.HoldsFrameOfSize(_slots[i].CounterOffset, width, height))
            {
                frame = finished;
                slot = i;
            }
        }

        return slot >= 0;
    }

    /// <summary>The pixels of a <paramref name="width"/> by <paramref name="height"/> frame in <paramref name="slot"/>,
    /// rows packed, while this reader is not disposed.</summary>
    public ReadOnlySpan<byte> Pixels(int slot, uint width, uint height) =>
        new(_memory.At(_slots[slot].MemoryOffset), (int)((ulong)width * height * (ulong)_slots[slot].Format.SizeInBytes));

    /// <summary>Gives every slot holding <paramref name="frame"/> or an older one back to the producer.</summary>
    public void GiveBack(ulong frame) => GiveBack(frame, []);

    /// <summary>Gives every slot holding <paramref name="frame"/> or an older one back to the producer, but those still
    /// copied from: a slot whose count in <paramref name="copying"/> is not 0.</summary>
    public void GiveBack(ulong frame, ReadOnlySpan<int> copying)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (i < copying.Length && copying[i] != 0)
            {
                continue;
            }

            var finished = _memory.ReadCounter(_slots[i].CounterOffset);
            if (finished <= frame)
            {
                _memory.WriteCounter(_slots[i].CounterOffset + sizeof(ulong), finished);
            }
        }
    }

    public void Dispose() => _memory.Dispose();
}
