using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The application's notifications.</summary>
public static unsafe class NSNotificationCenter
{
    private static readonly IntPtr DefaultCenterSelector = ObjCRuntime.RegisterSelector("defaultCenter");
    private static readonly IntPtr AddObserverSelector =
        ObjCRuntime.RegisterSelector("addObserver:selector:name:object:");

    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSNotificationCenter");

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
}
