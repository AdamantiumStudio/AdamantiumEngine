using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The application's notifications, and those of every application.</summary>
public static unsafe class NSNotificationCenter
{
    private const nuint DeliverImmediately = 4;

    private static readonly IntPtr DefaultCenterSelector = ObjCRuntime.RegisterSelector("defaultCenter");
    private static readonly IntPtr AddObserverSelector =
        ObjCRuntime.RegisterSelector("addObserver:selector:name:object:");
    private static readonly IntPtr AddDistributedObserverSelector =
        ObjCRuntime.RegisterSelector("addObserver:selector:name:object:suspensionBehavior:");

    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSNotificationCenter");
    private static readonly IntPtr DistributedClass = ObjCFrameworks.GetClass("NSDistributedNotificationCenter");

    /// <summary>Sends <paramref name="selector"/> to <paramref name="observer"/>, which is not retained, at every
    /// notification of <paramref name="name"/>.</summary>
    public static void AddObserver(IntPtr observer, string selector, string name)
    {
        var text = NSString.Create(name);
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(
            NSObject.Send(Class, DefaultCenterSelector), AddObserverSelector, observer,
            ObjCRuntime.RegisterSelector(selector), text, IntPtr.Zero);
        NSObject.Release(text);
    }

    /// <summary>The same at notifications other applications and the system post to all; told on the main thread, while
    /// the application is not the active one too.</summary>
    public static void AddDistributedObserver(IntPtr observer, string selector, string name)
    {
        var text = NSString.Create(name);
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, nuint, void>)ObjCRuntime.MessageSend)(
            NSObject.Send(DistributedClass, DefaultCenterSelector), AddDistributedObserverSelector, observer,
            ObjCRuntime.RegisterSelector(selector), text, IntPtr.Zero, DeliverImmediately);
        NSObject.Release(text);
    }
}
