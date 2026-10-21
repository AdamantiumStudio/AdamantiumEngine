using System.Runtime.InteropServices;

namespace Adamantium.Wayland.Native;

/// <summary>
/// The part of libwayland-client the protocols go through. It holds no protocol of its own: every request of every
/// interface is one call of wl_proxy_marshal_array_flags with that interface's table, and every event arrives at one
/// dispatcher - so this is all of it, and the generated code is the rest.
/// </summary>
internal static unsafe class WaylandClient
{
    // The runtime's soname: the unversioned libwayland-client.so comes only with -devel.
    private const string Library = "libwayland-client.so.0";

    /// <summary>WL_MARSHAL_FLAG_DESTROY: the request is the object's destructor, and the proxy goes with it.</summary>
    public const uint MarshalFlagDestroy = 1;

    [DllImport(Library)]
    public static extern nint wl_display_connect(byte* name);

    [DllImport(Library)]
    public static extern void wl_display_disconnect(nint display);

    [DllImport(Library)]
    public static extern int wl_display_get_fd(nint display);

    [DllImport(Library)]
    public static extern int wl_display_dispatch(nint display);

    [DllImport(Library)]
    public static extern int wl_display_dispatch_pending(nint display);

    [DllImport(Library)]
    public static extern int wl_display_roundtrip(nint display);

    [DllImport(Library)]
    public static extern int wl_display_flush(nint display);

    [DllImport(Library)]
    public static extern int wl_display_prepare_read(nint display);

    [DllImport(Library)]
    public static extern int wl_display_read_events(nint display);

    [DllImport(Library)]
    public static extern void wl_display_cancel_read(nint display);

    [DllImport(Library)]
    public static extern int wl_display_get_error(nint display);

    [DllImport(Library)]
    public static extern uint wl_display_get_protocol_error(nint display, WlInterface** @interface, uint* id);

    [DllImport(Library)]
    public static extern nint wl_proxy_marshal_array_flags(nint proxy, uint opcode, WlInterface* @interface,
        uint version, uint flags, WlArgument* args);

    [DllImport(Library)]
    public static extern int wl_proxy_add_dispatcher(nint proxy,
        delegate* unmanaged[Cdecl]<nint, nint, uint, WlMessage*, WlArgument*, int> dispatcher, nint dispatcherData,
        nint data);

    [DllImport(Library)]
    public static extern void wl_proxy_destroy(nint proxy);

    [DllImport(Library)]
    public static extern uint wl_proxy_get_id(nint proxy);
}
