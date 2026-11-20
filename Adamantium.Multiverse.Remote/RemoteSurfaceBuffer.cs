namespace Adamantium.Multiverse.Remote;

/// <summary>One surface of a <see cref="RemoteSurface"/>: the handles of its memory and of its two timelines, valid in
/// the viewer's process.</summary>
public readonly record struct RemoteSurfaceBuffer(
    long MemoryHandle,
    ulong AllocationSize,
    long ProduceSemaphoreHandle,
    long ConsumeSemaphoreHandle);
