using System.Runtime.InteropServices;

namespace Adamantium.Wayland;

/// <summary>struct wl_interface: an interface's name, version and the messages of each direction.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WlInterface
{
    public byte* name;
    public int version;
    public int method_count;
    public WlMessage* methods;
    public int event_count;
    public WlMessage* events;
}

/// <summary>struct wl_message: a request or an event, its wire signature and the interfaces of its object and new_id
/// arguments, one slot for each letter of the signature.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WlMessage
{
    public byte* name;
    public byte* signature;
    public WlInterface** types;
}

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

/// <summary>wl_fixed_t: a signed 24.8 fixed-point number, the protocol's coordinates.</summary>
public static class WlFixed
{
    public static double ToDouble(int value) => value / 256.0;

    public static int FromDouble(double value) => (int)Math.Round(value * 256.0);
}

/// <summary>A string marshalled to the wire as UTF-8 for the length of one request; null stays null.</summary>
public readonly unsafe ref struct WaylandString
{
    public WaylandString(string? value)
    {
        Pointer = value == null ? null : (byte*)Marshal.StringToCoTaskMemUTF8(value);
    }

    public byte* Pointer { get; }

    public void Dispose()
    {
        if (Pointer != null)
        {
            Marshal.FreeCoTaskMem((nint)Pointer);
        }
    }

    public static string? Read(byte* value) => value == null ? null : Marshal.PtrToStringUTF8((nint)value);
}
