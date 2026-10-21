using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Adamantium.Wayland.Native;

namespace Adamantium.Wayland;

/// <summary>What every generated interface class provides about itself, so that one can be made by its type: bound
/// from the registry, or created by a request.</summary>
public unsafe interface IWaylandInterface<TSelf> where TSelf : WaylandProxy, IWaylandInterface<TSelf>
{
    static abstract WlInterface* Interface { get; }

    static abstract TSelf Create(nint handle, uint version);
}

/// <summary>
/// A client object of the protocol: a wl_proxy, and the managed object its events are raised on.
/// <para>
/// Events reach every proxy of ours through one dispatcher, which libwayland calls with the proxy's GCHandle; the
/// generated class turns the arguments into its event's. An object an event names is found by its handle among ours -
/// a proxy someone else made on the display (Mesa's, for the swapchain) is not, and comes out as null.
/// </para>
/// </summary>
public abstract unsafe class WaylandProxy
{
    private static readonly ConcurrentDictionary<nint, WaylandProxy> Proxies = new();

    [ThreadStatic] private static ExceptionDispatchInfo dispatchFailure;

    private GCHandle self;
    private bool owned = true;

    protected WaylandProxy(nint handle, uint version)
    {
        Handle = handle;
        Version = version;
    }

    /// <summary>The wl_proxy, or 0 once destroyed.</summary>
    public nint Handle { get; private set; }

    /// <summary>The version the object was made with, which its requests and events are limited to.</summary>
    public uint Version { get; }

    /// <summary>The object's id on the wire.</summary>
    public uint Id => Handle == 0 ? 0 : WaylandClient.wl_proxy_get_id(Handle);

    public bool IsAlive => Handle != 0;

    /// <summary>Destroys the proxy on the client side alone - for an object whose interface has no destructor
    /// request, or one the compositor already destroyed. Where there is a destructor request, call that.</summary>
    public void DestroyProxy()
    {
        if (Handle == 0)
        {
            return;
        }

        if (owned)
        {
            WaylandClient.wl_proxy_destroy(Handle);
        }

        Forget();
    }

    public override string ToString() => $"{GetType().Name}@{Id}";

    protected abstract void DispatchEvent(uint opcode, WlArgument* args);

    /// <summary>Sends a request. A request that creates an object returns its new wl_proxy.</summary>
    protected nint Marshal(uint opcode, WlInterface* newInterface, uint version, WlArgument* args)
    {
        ThrowIfDestroyed();
        return WaylandClient.wl_proxy_marshal_array_flags(Handle, opcode, newInterface, version, 0, args);
    }

    /// <summary>Sends the object's destructor request; libwayland destroys the proxy with it.</summary>
    protected void MarshalDestroy(uint opcode, WlArgument* args)
    {
        ThrowIfDestroyed();
        WaylandClient.wl_proxy_marshal_array_flags(Handle, opcode, null, Version, WaylandClient.MarshalFlagDestroy, args);
        Forget();
    }

    /// <summary>Takes on the proxy a request has just created.</summary>
    protected static T Create<T>(nint handle, uint version) where T : WaylandProxy, IWaylandInterface<T>
    {
        if (handle == 0)
        {
            throw new WaylandException($"The display did not make a {typeof(T).Name}: it is out of memory or has failed.");
        }

        var proxy = T.Create(handle, version);
        proxy.Attach();
        return proxy;
    }

    /// <summary>Takes on the proxy libwayland made for an object an event announces.</summary>
    protected static T Adopt<T>(nint handle, uint version) where T : WaylandProxy, IWaylandInterface<T>
    {
        return Create<T>(handle, version);
    }

    /// <summary>The proxy of ours with this handle, null if there is none - or if it is someone else's.</summary>
    protected static T Find<T>(nint handle) where T : WaylandProxy
    {
        return handle != 0 && Proxies.TryGetValue(handle, out var proxy) ? proxy as T : null;
    }

    /// <summary>Wraps a proxy libwayland owns and dispatches itself - the display - without taking it over.</summary>
    internal static T Wrap<T>(nint handle, uint version) where T : WaylandProxy, IWaylandInterface<T>
    {
        var proxy = T.Create(handle, version);
        proxy.owned = false;
        Proxies[handle] = proxy;
        return proxy;
    }

    /// <summary>Rethrows, on the thread that dispatched, what a handler threw while libwayland was calling it.</summary>
    internal static void ThrowDispatchFailure()
    {
        var failure = dispatchFailure;
        dispatchFailure = null;
        failure?.Throw();
    }

    private void Attach()
    {
        self = GCHandle.Alloc(this);
        Proxies[Handle] = this;
        if (WaylandClient.wl_proxy_add_dispatcher(Handle, &OnEvent, GCHandle.ToIntPtr(self), 0) != 0)
        {
            Forget();
            throw new WaylandException($"{GetType().Name} already has a listener.");
        }
    }

    private void Forget()
    {
        Proxies.TryRemove(Handle, out _);
        if (self.IsAllocated)
        {
            self.Free();
        }

        Handle = 0;
    }

    private void ThrowIfDestroyed()
    {
        if (Handle == 0)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnEvent(nint implementation, nint target, uint opcode, WlMessage* message, WlArgument* args)
    {
        try
        {
            if (GCHandle.FromIntPtr(implementation).Target is WaylandProxy proxy && proxy.Handle != 0)
            {
                proxy.DispatchEvent(opcode, args);
            }
        }
        catch (Exception e)
        {
            // An exception must not unwind through libwayland. The first one is kept and rethrown when dispatch returns.
            dispatchFailure ??= ExceptionDispatchInfo.Capture(e);
        }

        return 0;
    }
}
