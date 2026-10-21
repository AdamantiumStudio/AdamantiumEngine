using System.Runtime.InteropServices;

namespace Adamantium.Wayland;

/// <summary>struct wl_message: a request or an event, its wire signature and the interfaces of its object and new_id
/// arguments, one slot for each letter of the signature.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct WlMessage
{
    public byte* name;
    public byte* signature;
    public WlInterface** types;
}
