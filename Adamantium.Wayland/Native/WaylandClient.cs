using System.Runtime.InteropServices;

namespace Adamantium.Wayland.Native;

/// <summary>The part of libwayland-client the generated protocols go through.</summary>
internal static unsafe class WaylandClient
{
    private const string Library = "libwayland-client.so.0";

    /// <summary>WL_MARSHAL_FLAG_DESTROY: the request is the object's destructor, and the proxy goes with it.</summary>
    public const uint MarshalFlagDestroy = 1;

    [DllImport(Library, EntryPoint = "wl_display_connect")]
    public static extern nint DisplayConnect(byte* name);

    [DllImport(Library, EntryPoint = "wl_display_disconnect")]
    public static extern void DisplayDisconnect(nint display);

    [DllImport(Library, EntryPoint = "wl_display_get_fd")]
    public static extern int DisplayGetFd(nint display);

    [DllImport(Library, EntryPoint = "wl_display_dispatch")]
    public static extern int DisplayDispatch(nint display);

    [DllImport(Library, EntryPoint = "wl_display_dispatch_pending")]
    public static extern int DisplayDispatchPending(nint display);

    [DllImport(Library, EntryPoint = "wl_display_roundtrip")]
    public static extern int DisplayRoundtrip(nint display);

    [DllImport(Library, EntryPoint = "wl_display_flush")]
    public static extern int DisplayFlush(nint display);

    [DllImport(Library, EntryPoint = "wl_display_prepare_read")]
    public static extern int DisplayPrepareRead(nint display);

    [DllImport(Library, EntryPoint = "wl_display_read_events")]
    public static extern int DisplayReadEvents(nint display);

    [DllImport(Library, EntryPoint = "wl_display_cancel_read")]
    public static extern void DisplayCancelRead(nint display);

    [DllImport(Library, EntryPoint = "wl_display_get_error")]
    public static extern int DisplayGetError(nint display);

    [DllImport(Library, EntryPoint = "wl_display_get_protocol_error")]
    public static extern uint DisplayGetProtocolError(nint display, WlInterface** @interface, uint* id);

    [DllImport(Library, EntryPoint = "wl_proxy_marshal_array_flags")]
    public static extern nint ProxyMarshalArrayFlags(nint proxy, uint opcode, WlInterface* @interface,
        uint version, uint flags, WlArgument* args);

    [DllImport(Library, EntryPoint = "wl_proxy_add_dispatcher")]
    public static extern int ProxyAddDispatcher(nint proxy,
        delegate* unmanaged[Cdecl]<nint, nint, uint, WlMessage*, WlArgument*, int> dispatcher, nint dispatcherData,
        nint data);

    [DllImport(Library, EntryPoint = "wl_proxy_destroy")]
    public static extern void ProxyDestroy(nint proxy);

    [DllImport(Library, EntryPoint = "wl_proxy_get_id")]
    public static extern uint ProxyGetId(nint proxy);
}
