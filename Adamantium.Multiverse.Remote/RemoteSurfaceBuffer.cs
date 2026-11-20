namespace Adamantium.Multiverse.Remote;

/// <summary>One surface of a <see cref="RemoteSurface"/>: the handles of its memory and of its two timelines, valid in
/// the viewer's process; or, for a ring in memory of the process, where its pixels and its counters are in the file.
/// </summary>
public readonly record struct RemoteSurfaceBuffer(
    long MemoryHandle,
    ulong AllocationSize,
    long ProduceSemaphoreHandle,
    long ConsumeSemaphoreHandle,
    ulong MemoryOffset = 0,
    ulong CounterOffset = 0);
