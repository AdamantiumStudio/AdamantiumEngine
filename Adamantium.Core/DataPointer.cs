using System;

namespace Adamantium.Core;

/// <summary>Bytes somewhere in memory: where they start, how many there are and, when they are elements, how many of
/// those.</summary>
public record struct DataPointer(IntPtr Pointer, ulong Size, uint Count = 0)
{
    /// <summary>Whether it points at nothing.</summary>
    public readonly bool IsEmpty => Pointer == IntPtr.Zero && Size == 0;
}
