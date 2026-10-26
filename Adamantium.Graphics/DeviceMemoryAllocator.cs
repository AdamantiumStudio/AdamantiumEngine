using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;
using Serilog;

namespace Adamantium.Graphics;

/// <summary>Sub-allocates buffer memory from a few large blocks, staying under Vulkan's <c>maxMemoryAllocationCount</c>.
/// Host-visible blocks are mapped once for life. Buffers only.</summary>
public sealed class DeviceMemoryAllocator : IDeviceMemoryAllocator
{
    // 64 MB blocks, but never more than a 64th of the heap: the host-visible BAR window (~214 MB without Resizable BAR) is
    // shared by every process on the GPU, so blocks there stay at the floor. A larger allocation gets a block of its own.
    private const ulong DefaultBlockSize = 64UL * 1024 * 1024;
    private const ulong MinBlockSize = 4UL * 1024 * 1024;
    private const ulong HeapShareOfBlock = 64;
    private const uint NoType = uint.MaxValue;

    private readonly IGraphicsDevice _device;
    private readonly object _lock = new();
    private readonly Dictionary<int, List<Block>> _groups = new();   // key = GroupKey(memoryTypeIndex, deviceAddress)
    private readonly ulong[] _heapSizeByType;   // heap byte size behind each memory-type index (block-size cap)
    private readonly uint[] _heapIndexByType;
    private readonly MemoryPropertyFlags[] _flagsByType;
    private readonly ulong[] _blockBytesByHeap;
    private readonly bool[] _fellBackFromType;

    public DeviceMemoryAllocator(IGraphicsDevice device)
    {
        _device = device;
        var memProps = device.Adapter.Adapter.GetPhysicalDeviceMemoryProperties();
        _heapSizeByType = new ulong[memProps.MemoryTypeCount];
        _heapIndexByType = new uint[memProps.MemoryTypeCount];
        _flagsByType = new MemoryPropertyFlags[memProps.MemoryTypeCount];
        _fellBackFromType = new bool[memProps.MemoryTypeCount];
        _blockBytesByHeap = new ulong[memProps.MemoryHeapCount];
        for (var t = 0; t < memProps.MemoryTypeCount; t++)
        {
            var type = memProps.MemoryTypes.Span[t];
            _heapIndexByType[t] = type.HeapIndex;
            _flagsByType[t] = (MemoryPropertyFlags)type.PropertyFlags;
            _heapSizeByType[t] = memProps.MemoryHeaps.Span[(int)type.HeapIndex].Size;
        }
    }

    /// <summary>Reserves <paramref name="size"/> bytes (aligned to <paramref name="alignment"/>) inside a shared block of
    /// the given memory type, allocating a new block only when no existing one has room. <paramref name="hostVisible"/>
    /// blocks are persistently mapped; <paramref name="deviceAddress"/> selects a block allocated with the BDA flag.
    /// When the device-local host-visible window is full - it is shared by every process on the GPU - the block comes
    /// from a host-visible type in system memory that <paramref name="memoryTypeBits"/> allows.</summary>
    public MemoryAllocation Allocate(ulong size, ulong alignment, uint memoryTypeIndex, uint memoryTypeBits, bool hostVisible, bool deviceAddress)
    {
        if (size == 0) size = 1;
        if (alignment == 0) alignment = 1;
        size = AlignUp(size, alignment);

        lock (_lock)
        {
            var allocation = TryAllocate(size, alignment, memoryTypeIndex, hostVisible, deviceAddress);
            if (allocation != null)
            {
                return allocation;
            }

            var fallback = FallbackType(memoryTypeIndex, memoryTypeBits);
            if (fallback != NoType)
            {
                allocation = TryAllocate(size, alignment, fallback, hostVisible, deviceAddress);
                if (allocation != null)
                {
                    WarnFallback(memoryTypeIndex, fallback);
                    return allocation;
                }
            }

            var heap = _heapIndexByType[memoryTypeIndex];
            throw new GraphicsEngineException(
                $"Out of GPU memory: {size} bytes in memory type {memoryTypeIndex} (heap {heap}, " +
                $"{_blockBytesByHeap[heap] / 1048576.0:F1} of {_heapSizeByType[memoryTypeIndex] / 1048576} MB in this process's blocks), " +
                "and no other memory type the buffer allows can take it");
        }
    }

    private MemoryAllocation TryAllocate(ulong size, ulong alignment, uint memoryTypeIndex, bool hostVisible, bool deviceAddress)
    {
        var key = GroupKey(memoryTypeIndex, deviceAddress);
        if (!_groups.TryGetValue(key, out var blocks))
        {
            blocks = [];
            _groups[key] = blocks;
        }

        foreach (var block in blocks)
        {
            if (TryCarve(block, size, alignment, out var offset))
            {
                return MakeAllocation(block, offset, size);
            }
        }

        var heapCap = Math.Max(MinBlockSize, _heapSizeByType[memoryTypeIndex] / HeapShareOfBlock);
        var sharedSize = Math.Min(DefaultBlockSize, heapCap);
        var blockSize = Math.Max(sharedSize, size);
        var fresh = TryCreateBlock(blockSize, memoryTypeIndex, hostVisible, deviceAddress);
        if (fresh == null)
        {
            return null;
        }

        fresh.Group = blocks;
        fresh.TypeIndex = memoryTypeIndex;
        fresh.Own = blockSize > sharedSize;
        blocks.Add(fresh);
        if (!TryCarve(fresh, size, alignment, out var freshOffset))
        {
            throw new GraphicsEngineException("Fresh GPU memory block could not satisfy its own allocation");
        }

        return MakeAllocation(fresh, freshOffset, size);
    }

    private uint FallbackType(uint failedType, uint memoryTypeBits)
    {
        var failed = _flagsByType[failedType];
        if (!failed.HasFlag(MemoryPropertyFlags.DeviceLocal) || !failed.HasFlag(MemoryPropertyFlags.HostVisible))
        {
            return NoType;
        }

        var wanted = failed & (MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
        for (uint t = 0; t < _flagsByType.Length; t++)
        {
            var allowed = ((memoryTypeBits >> (int)t) & 1) == 1;
            var flags = _flagsByType[t];
            if (t != failedType && allowed && !flags.HasFlag(MemoryPropertyFlags.DeviceLocal) && flags.HasFlag(wanted))
            {
                return t;
            }
        }

        return NoType;
    }

    private void WarnFallback(uint wanted, uint taken)
    {
        if (_fellBackFromType[wanted])
        {
            return;
        }

        _fellBackFromType[wanted] = true;
        Log.Logger.Warning(
            "[MEM] memory type {Wanted} (heap {Heap}) is full: its buffers now take type {Taken} (heap {TakenHeap}), which the GPU reads more slowly",
            wanted, _heapIndexByType[wanted], taken, _heapIndexByType[taken]);
    }

    /// <summary>Returns a sub-allocation's range to its block's free list (coalescing neighbors). A shared block is
    /// retained for reuse until <see cref="Dispose"/>; a block made for one allocation larger than a shared block goes
    /// back to the driver once that allocation is freed.</summary>
    public void Free(MemoryAllocation allocation)
    {
        if (allocation?.Block == null) return;
        lock (_lock)
        {
            var block = allocation.Block;
            InsertFree(block, allocation.Offset, allocation.Size);
            allocation.Block = null;
            if (block.Own && block.Free.Count == 1 && block.Free[0].Size == block.Size)
            {
                Destroy(block);
                block.Group.Remove(block);
            }
        }
    }

    /// <summary>The bytes this allocator holds in blocks, over every heap.</summary>
    internal ulong BytesInBlocks
    {
        get
        {
            lock (_lock)
            {
                ulong total = 0;
                foreach (var bytes in _blockBytesByHeap)
                {
                    total += bytes;
                }

                return total;
            }
        }
    }

    private void Destroy(Block block)
    {
        if (block.MappedBase != 0)
        {
            _device.UnmapMemory(block.Memory);
        }

        _device.Destroy(block.Memory);
        _blockBytesByHeap[_heapIndexByType[block.TypeIndex]] -= block.Size;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var blocks in _groups.Values)
            {
                foreach (var block in blocks)
                {
                    Destroy(block);
                }
            }

            _groups.Clear();
        }
    }

    private MemoryAllocation MakeAllocation(Block block, ulong offset, ulong size) => new()
    {
        Block = block,
        Memory = block.Memory,
        Offset = offset,
        Size = size,
        MappedBase = block.MappedBase == 0 ? 0 : block.MappedBase + (nuint)offset,
    };

    private Block TryCreateBlock(ulong size, uint memoryTypeIndex, bool hostVisible, bool deviceAddress)
    {
        var allocInfo = new MemoryAllocateInfo { AllocationSize = size, MemoryTypeIndex = memoryTypeIndex };
        if (deviceAddress)
            allocInfo.PNext = new MemoryAllocateFlagsInfo { Flags = MemoryAllocateFlagBits.DeviceAddressBit };

        var result = _device.LogicalDevice.AllocateMemory(allocInfo, null, out var memory);
        if (result == Result.ErrorOutOfDeviceMemory)
        {
            return null;
        }

        if (result != Result.Success)
        {
            throw new GraphicsEngineException($"vkAllocateMemory returned {result} for {size} bytes in memory type {memoryTypeIndex}");
        }

        nuint mappedBase = hostVisible ? _device.MapMemory(memory, 0, size, 0) : 0;

        var heap = _heapIndexByType[memoryTypeIndex];
        _blockBytesByHeap[heap] += size;
        Log.Logger.Debug("[MEM] block {Size:F1} MB in type {Type} (heap {Heap}): {Total:F1} of {HeapSize} MB in blocks",
            size / 1048576.0, memoryTypeIndex, heap, _blockBytesByHeap[heap] / 1048576.0, _heapSizeByType[memoryTypeIndex] / 1048576);

        return new Block { Memory = memory, Size = size, MappedBase = mappedBase, Free = [new FreeRange(0, size)] };
    }

    // Find the first free range that fits (respecting alignment); carve [alignedOffset, alignedOffset+size) out of it,
    // leaving the alignment pad and the tail as (smaller) free ranges. First-fit is fine for the modest UI churn.
    private static bool TryCarve(Block block, ulong size, ulong alignment, out ulong dataOffset)
    {
        var free = block.Free;
        for (var i = 0; i < free.Count; i++)
        {
            var r = free[i];
            var aligned = AlignUp(r.Offset, alignment);
            var pad = aligned - r.Offset;
            if (pad + size > r.Size) continue;   // doesn't fit here (including alignment padding)

            dataOffset = aligned;
            var tailOffset = aligned + size;
            var tailSize = r.Offset + r.Size - tailOffset;
            free.RemoveAt(i);
            if (tailSize > 0) free.Insert(i, new FreeRange(tailOffset, tailSize));
            if (pad > 0) free.Insert(i, new FreeRange(r.Offset, pad));
            return true;
        }
        dataOffset = 0;
        return false;
    }

    // Insert [offset, offset+size) back into the sorted free list, merging with an adjacent range on either side.
    private static void InsertFree(Block block, ulong offset, ulong size)
    {
        var free = block.Free;
        var i = 0;
        while (i < free.Count && free[i].Offset < offset) i++;
        free.Insert(i, new FreeRange(offset, size));

        if (i > 0 && free[i - 1].Offset + free[i - 1].Size == free[i].Offset)   // merge with previous
        {
            free[i - 1] = new FreeRange(free[i - 1].Offset, free[i - 1].Size + free[i].Size);
            free.RemoveAt(i);
            i--;
        }
        if (i + 1 < free.Count && free[i].Offset + free[i].Size == free[i + 1].Offset)   // merge with next
        {
            free[i] = new FreeRange(free[i].Offset, free[i].Size + free[i + 1].Size);
            free.RemoveAt(i + 1);
        }
    }

    private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static int GroupKey(uint memoryTypeIndex, bool deviceAddress) => (int)memoryTypeIndex * 2 + (deviceAddress ? 1 : 0);

    internal sealed class Block
    {
        public DeviceMemory Memory;
        public ulong Size;
        public nuint MappedBase;   // persistent map base, or 0 for a device-local (non-host-visible) block
        public List<FreeRange> Free;
        public List<Block> Group;
        public uint TypeIndex;
        public bool Own;
    }

    internal readonly record struct FreeRange(ulong Offset, ulong Size);

    /// <summary>Handle to one sub-allocation: the shared block memory, the byte offset the buffer is bound at, and (for a
    /// host-visible block) the mapped CPU pointer to that offset. Returned by <see cref="Allocate"/>, passed to <see cref="Free"/>.</summary>
    public sealed class MemoryAllocation
    {
        internal Block Block;
        public DeviceMemory Memory;
        public ulong Offset;
        public ulong Size;
        public nuint MappedBase;   // Block.MappedBase + Offset, or 0 if the block is not host-visible
    }
}
