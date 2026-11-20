using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace Adamantium.Graphics;

/// <summary>Memory shared between processes through a file, mapped into each: a header of two counters per slot - the
/// frame the producer finished in it, the frame the consumer read from it - then the slots, each a frame's pixels, rows
/// packed. Every slot starts and ends on <c>alignment</c>, as memory imported into a GPU must.</summary>
public sealed unsafe class SharedFrameMemory : IDisposable
{
    private const ulong CounterSize = 2 * sizeof(ulong);
    private const string SharedMemoryFolder = "/dev/shm";

    private readonly FileStream _file;
    private readonly MemoryMappedFile _mapping;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _start;

    private SharedFrameMemory(string path, FileStream file, ulong headerSize, ulong slotSize, uint slotCount)
    {
        Path = path;
        _file = file;
        HeaderSize = headerSize;
        SlotSize = slotSize;
        SlotCount = slotCount;
        _mapping = MemoryMappedFile.CreateFromFile(file, null, (long)(headerSize + slotSize * slotCount),
            MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
        _view = _mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        byte* start = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref start);
        _start = start + _view.PointerOffset;
    }

    /// <summary>The file the memory is shared through.</summary>
    public string Path { get; }

    public ulong HeaderSize { get; }

    public ulong SlotSize { get; }

    public uint SlotCount { get; }

    /// <summary>Makes memory for <paramref name="slotCount"/> slots of <paramref name="frameSize"/> bytes, in a new file
    /// kept in memory where the system has a place for it - /dev/shm on Linux, a temporary file on Windows the cache keeps
    /// - gone once every process let go of it; on Unix only its user reads it.</summary>
    public static SharedFrameMemory Create(ulong frameSize, uint slotCount, ulong alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentException("The alignment is a power of two.", nameof(alignment));
        }

        var headerSize = AlignUp(CounterSize * slotCount, alignment);
        var slotSize = AlignUp(frameSize, alignment);
        var folder = OperatingSystem.IsLinux() && Directory.Exists(SharedMemoryFolder)
            ? SharedMemoryFolder
            : System.IO.Path.GetTempPath();
        var path = System.IO.Path.Combine(folder, "adamantium-frames-" + Guid.NewGuid().ToString("N"));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var file = new FileStream(path, options);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(path, FileAttributes.Temporary | FileAttributes.NotContentIndexed);
            }

            file.SetLength((long)(headerSize + slotSize * slotCount));
            return new SharedFrameMemory(path, file, headerSize, slotSize, slotCount);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>Maps the memory another process made at <paramref name="path"/>, laid out as it said.</summary>
    public static SharedFrameMemory Open(string path, ulong headerSize, ulong slotSize, uint slotCount)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        return new SharedFrameMemory(path, file, headerSize, slotSize, slotCount);
    }

    /// <summary>Where slot <paramref name="slot"/>'s pixels start, from the start of the memory.</summary>
    public ulong SlotOffset(uint slot) => HeaderSize + SlotSize * slot;

    /// <summary>Where slot <paramref name="slot"/>'s counters are, from the start of the memory.</summary>
    public ulong CounterOffset(uint slot) => CounterSize * slot;

    /// <summary>The memory at <paramref name="offset"/>, for as long as this is not disposed.</summary>
    public byte* At(ulong offset) => _start + offset;

    /// <summary>The counter at <paramref name="offset"/>, as the other process last wrote it.</summary>
    public ulong ReadCounter(ulong offset) => Volatile.Read(ref *(ulong*)At(offset));

    /// <summary>Writes the counter at <paramref name="offset"/>, after everything written before it.</summary>
    public void WriteCounter(ulong offset, ulong value) => Volatile.Write(ref *(ulong*)At(offset), value);

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _mapping.Dispose();
        _file.Dispose();
    }

    private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);
}
