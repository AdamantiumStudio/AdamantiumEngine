using System.Runtime.InteropServices;

namespace Adamantium.MacOS.AudioToolbox;

/// <summary>The start of an <c>AudioQueueBuffer</c>, which the queue allocates.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AudioQueueBuffer
{
    public uint Capacity;
    public void* Data;
    public uint Size;
}
