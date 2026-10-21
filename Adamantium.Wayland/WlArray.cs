using System.Runtime.InteropServices;

namespace Adamantium.Wayland;

/// <summary>struct wl_array: a protocol array, bytes whose meaning the message gives - xdg_toplevel's states are
/// uint32 values.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WlArray
{
    public nuint size;
    public nuint alloc;
    public void* data;

    public WlArray(void* data, int length)
    {
        this.data = data;
        size = (nuint)length;
        alloc = (nuint)length;
    }

    public readonly ReadOnlySpan<byte> AsSpan() => new(data, (int)size);
}
