using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The application's notifications, and those of every application.</summary>
public static unsafe class NSNotificationCenter
{
    private static readonly IntPtr DefaultCenterSelector = ObjCRuntime.RegisterSelector("defaultCenter");
    private static readonly IntPtr AddObserverSelector =
        ObjCRuntime.RegisterSelector("addObserver:selector:name:object:");

    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSNotificationCenter");
    private static readonly IntPtr DistributedClass = ObjCFrameworks.GetClass("NSDistributedNotificationCenter");

    /// <summary>Sends <paramref name="selector"/> to <paramref name="observer"/>, which is not retained, at every
    /// notification of <paramref name="name"/>.</summary>
    public static void AddObserver(IntPtr observer, string selector, string name) =>
        AddObserver(Class, observer, selector, name);

    /// <summary>The same at notifications other applications and the system post to all; told on the main thread.</summary>
    public static void AddDistributedObserver(IntPtr observer, string selector, string name) =>
        AddObserver(DistributedClass, observer, selector, name);

    private static void AddObserver(IntPtr center, IntPtr observer, string selector, string name)
    {
        var text = NSString.Create(name);
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(
            NSObject.Send(center, DefaultCenterSelector), AddObserverSelector, observer,
            ObjCRuntime.RegisterSelector(selector), text, IntPtr.Zero);
        NSObject.Release(text);
    }
}
