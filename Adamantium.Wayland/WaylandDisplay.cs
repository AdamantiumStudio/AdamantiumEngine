using Adamantium.Wayland.Native;

namespace Adamantium.Wayland;

/// <summary>The connection to the compositor; a handler that throws has its exception rethrown from the dispatching
/// call.</summary>
public sealed unsafe class WaylandDisplay : IDisposable
{
    private WaylandDisplay(nint handle)
    {
        Handle = handle;
        Proxy = WaylandProxy.Wrap<WlDisplay>(handle, 1);
    }

    /// <summary>The wl_display*: what a Vulkan surface is made on, beside the wl_surface.</summary>
    public nint Handle { get; private set; }

    /// <summary>The display as an object of the protocol: its sync and get_registry requests.</summary>
    public WlDisplay Proxy { get; }

    /// <summary>The socket's file descriptor, to wait on for events.</summary>
    public int FileDescriptor => WaylandClient.DisplayGetFd(Handle);

    /// <summary>Connects to the compositor - the one WAYLAND_DISPLAY names when <paramref name="name"/> is null.</summary>
    public static WaylandDisplay Connect(string name = null)
    {
        return TryConnect(name) ?? throw new WaylandException(
            "No Wayland compositor to connect to" + (name == null ? " (WAYLAND_DISPLAY)" : $" at '{name}'") + ".");
    }

    /// <summary>Connects to the compositor, or returns null where there is none - an X11 session, a console.</summary>
    public static WaylandDisplay TryConnect(string name = null)
    {
        using var nameUtf8 = new WaylandString(name);
        var handle = WaylandClient.DisplayConnect(nameUtf8.Pointer);
        return handle == 0 ? null : new WaylandDisplay(handle);
    }

    public WlRegistry GetRegistry() => Proxy.GetRegistry();

    /// <summary>Waits for events if there are none queued, then dispatches them. Returns how many.</summary>
    public int Dispatch() => Check(WaylandClient.DisplayDispatch(Handle));

    /// <summary>Dispatches what is already queued, without reading the socket.</summary>
    public int DispatchPending() => Check(WaylandClient.DisplayDispatchPending(Handle));

    /// <summary>Sends what is queued and waits until the compositor has handled it all, dispatching meanwhile.</summary>
    public int Roundtrip() => Check(WaylandClient.DisplayRoundtrip(Handle));

    /// <summary>Sends the queued requests. False when the socket is full: wait for it to be writable and flush again.</summary>
    public bool Flush()
    {
        if (WaylandClient.DisplayFlush(Handle) >= 0)
        {
            return true;
        }

        if (WaylandClient.DisplayGetError(Handle) == 0)
        {
            return false;
        }

        throw Failure();
    }

    /// <summary>Announces a read of the socket from a loop that waits on it itself. False when events are already
    /// queued: dispatch them with <see cref="DispatchPending"/> first.</summary>
    public bool PrepareRead() => WaylandClient.DisplayPrepareRead(Handle) == 0;

    /// <summary>Reads the events the socket has, after <see cref="PrepareRead"/> and a wait that found it readable.</summary>
    public void ReadEvents() => Check(WaylandClient.DisplayReadEvents(Handle));

    /// <summary>Gives up a read announced by <see cref="PrepareRead"/>.</summary>
    public void CancelRead() => WaylandClient.DisplayCancelRead(Handle);

    public void Dispose()
    {
        if (Handle == 0)
        {
            return;
        }

        Proxy.DestroyProxy();
        WaylandClient.DisplayDisconnect(Handle);
        Handle = 0;
    }

    private int Check(int result)
    {
        WaylandProxy.ThrowDispatchFailure();
        if (result < 0)
        {
            throw Failure();
        }

        return result;
    }

    private WaylandException Failure()
    {
        var error = WaylandClient.DisplayGetError(Handle);
        WlInterface* @interface = null;
        uint id = 0;
        var code = WaylandClient.DisplayGetProtocolError(Handle, &@interface, &id);

        if (@interface != null)
        {
            var name = WaylandString.Read(@interface->name);
            return new WaylandException($"The compositor ended the connection: protocol error {code} on {name}@{id}.");
        }

        return new WaylandException($"The connection to the compositor failed (errno {error}).");
    }
}
