using System.Runtime.InteropServices;

namespace Adamantium.Wayland;

/// <summary>A string marshalled to the wire as UTF-8 for the length of one request; null stays null.</summary>
public readonly unsafe ref struct WaylandString
{
    public WaylandString(string value)
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

    public static string Read(byte* value) => value == null ? null : Marshal.PtrToStringUTF8((nint)value);
}
