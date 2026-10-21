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
