using System.Runtime.InteropServices;

namespace Adamantium.Wayland;

/// <summary>union wl_argument: one argument of a request or an event, whichever its signature letter says.</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public unsafe struct WlArgument
{
    [FieldOffset(0)] public int i;
    [FieldOffset(0)] public uint u;
    [FieldOffset(0)] public int f;
    [FieldOffset(0)] public byte* s;
    [FieldOffset(0)] public nint o;
    [FieldOffset(0)] public uint n;
    [FieldOffset(0)] public WlArray* a;
    [FieldOffset(0)] public int h;
}
