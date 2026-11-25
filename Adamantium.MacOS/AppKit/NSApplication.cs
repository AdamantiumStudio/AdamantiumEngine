using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>The application object (NSApp): launching, activation and an event loop the caller drives - an event at a
/// time, so the caller can stop between them.</summary>
public static unsafe class NSApplication
{
    /// <summary>NSApplicationActivationPolicyRegular: a Dock icon and a menu bar.</summary>
    public const nint RegularActivationPolicy = 0;

    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSApplication");
    private static readonly IntPtr SharedApplicationSelector = ObjCRuntime.RegisterSelector("sharedApplication");
    private static readonly IntPtr SetActivationPolicySelector = ObjCRuntime.RegisterSelector("setActivationPolicy:");
    private static readonly IntPtr FinishLaunchingSelector = ObjCRuntime.RegisterSelector("finishLaunching");
    private static readonly IntPtr ActivateSelector = ObjCRuntime.RegisterSelector("activateIgnoringOtherApps:");
    private static readonly IntPtr NextEventSelector =
        ObjCRuntime.RegisterSelector("nextEventMatchingMask:untilDate:inMode:dequeue:");
    private static readonly IntPtr SendEventSelector = ObjCRuntime.RegisterSelector("sendEvent:");
    private static readonly IntPtr UpdateWindowsSelector = ObjCRuntime.RegisterSelector("updateWindows");
    private static readonly IntPtr PostEventSelector = ObjCRuntime.RegisterSelector("postEvent:atStart:");
    private static readonly IntPtr SetApplicationIconImageSelector =
        ObjCRuntime.RegisterSelector("setApplicationIconImage:");
    private static readonly IntPtr DefaultRunLoopMode = NSString.Create("kCFRunLoopDefaultMode");

    private static readonly IntPtr KeyWindowSelector = ObjCRuntime.RegisterSelector("keyWindow");
    private static readonly IntPtr SetDelegateSelector = ObjCRuntime.RegisterSelector("setDelegate:");

    /// <summary>The window that has the keyboard; zero when none has.</summary>
    public static IntPtr KeyWindow => NSObject.Send(Shared, KeyWindowSelector);

    /// <summary>Sets the application's delegate (not retained).</summary>
    public static void SetDelegate(IntPtr applicationDelegate) =>
        NSObject.Send(Shared, SetDelegateSelector, applicationDelegate);

    /// <summary>Sets the icon the Dock and the application switcher show (an NSImage, retained).</summary>
    public static void SetIconImage(IntPtr image) => NSObject.Send(Shared, SetApplicationIconImageSelector, image);

    /// <summary>[NSApplication sharedApplication], made on first use.</summary>
    public static IntPtr Shared => NSObject.Send(Class, SharedApplicationSelector);

    /// <summary>NSApplicationActivationPolicyProhibited: nothing in the Dock, no menu bar, never active.</summary>
    public const nint ProhibitedActivationPolicy = 2;

    /// <summary>Finishes launching the application, a regular one unless told otherwise; call once, on the main thread.
    /// </summary>
    public static void Launch(nint activationPolicy = RegularActivationPolicy)
    {
        var application = Shared;
        ((delegate* unmanaged<IntPtr, IntPtr, nint, byte>)ObjCRuntime.MessageSend)(application,
            SetActivationPolicySelector, activationPolicy);
        NSObject.Send(application, FinishLaunchingSelector);
    }

    /// <summary>Brings the application to the front.</summary>
    public static void Activate() => NSObject.Send(Shared, ActivateSelector, true);

    /// <summary>Waits for the next event and takes it from the queue; <paramref name="until"/> is an NSDate.</summary>
    public static IntPtr NextEvent(IntPtr until) =>
        ((delegate* unmanaged<IntPtr, IntPtr, ulong, IntPtr, IntPtr, byte, IntPtr>)ObjCRuntime.MessageSend)(Shared,
            NextEventSelector, ulong.MaxValue, until, DefaultRunLoopMode, 1);

    /// <summary>Hands an event to the window or view it is for.</summary>
    public static void SendEvent(IntPtr nsEvent)
    {
        NSObject.Send(Shared, SendEventSelector, nsEvent);
        NSObject.Send(Shared, UpdateWindowsSelector);
    }

    /// <summary>Puts an event in the queue, from any thread: it wakes a <see cref="NextEvent"/> waiting.</summary>
    public static void PostEvent(IntPtr nsEvent) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, void>)ObjCRuntime.MessageSend)(Shared, PostEventSelector,
            nsEvent, 0);
}
