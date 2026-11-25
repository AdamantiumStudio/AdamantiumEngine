using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>An application's icon in the menu bar. Main thread only.</summary>
public static unsafe class NSStatusItem
{
    private const double SquareLength = -2;
    private const nint EitherButtonUp = 1 << (int)NSEventType.LeftMouseUp | 1 << (int)NSEventType.RightMouseUp;

    private static readonly IntPtr BarClass = ObjCFrameworks.GetClass("NSStatusBar");
    private static readonly IntPtr SystemBarSelector = ObjCRuntime.RegisterSelector("systemStatusBar");
    private static readonly IntPtr CreateSelector = ObjCRuntime.RegisterSelector("statusItemWithLength:");
    private static readonly IntPtr RemoveSelector = ObjCRuntime.RegisterSelector("removeStatusItem:");
    private static readonly IntPtr ButtonSelector = ObjCRuntime.RegisterSelector("button");
    private static readonly IntPtr SetImageSelector = ObjCRuntime.RegisterSelector("setImage:");
    private static readonly IntPtr SetToolTipSelector = ObjCRuntime.RegisterSelector("setToolTip:");
    private static readonly IntPtr SetTargetSelector = ObjCRuntime.RegisterSelector("setTarget:");
    private static readonly IntPtr SetActionSelector = ObjCRuntime.RegisterSelector("setAction:");
    private static readonly IntPtr SendActionOnSelector = ObjCRuntime.RegisterSelector("sendActionOn:");

    /// <summary>A new icon in the menu bar, owned by the caller: <see cref="Remove"/> takes it away.</summary>
    public static IntPtr Create() => NSObject.Retain(
        ((delegate* unmanaged<IntPtr, IntPtr, double, IntPtr>)ObjCRuntime.MessageSend)(Bar, CreateSelector, SquareLength));

    /// <summary>Takes the icon out of the menu bar.</summary>
    public static void Remove(IntPtr item)
    {
        NSObject.Send(Bar, RemoveSelector, item);
        NSObject.Release(item);
    }

    /// <summary>The view a menu of the icon opens under.</summary>
    public static IntPtr ButtonOf(IntPtr item) => NSObject.Send(item, ButtonSelector);

    /// <summary>Sets what the icon shows (an NSImage, retained).</summary>
    public static void SetImage(IntPtr item, IntPtr image) => NSObject.Send(ButtonOf(item), SetImageSelector, image);

    /// <summary>Sets what the icon says when the pointer rests on it.</summary>
    public static void SetToolTip(IntPtr item, string toolTip)
    {
        var text = NSString.Create(toolTip);
        NSObject.Send(ButtonOf(item), SetToolTipSelector, text);
        NSObject.Release(text);
    }

    /// <summary>Sends <paramref name="action"/> to <paramref name="target"/>, not retained, at a click of either button.
    /// </summary>
    public static void SetAction(IntPtr item, IntPtr target, string action)
    {
        var button = ButtonOf(item);
        NSObject.Send(button, SetTargetSelector, target);
        NSObject.Send(button, SetActionSelector, ObjCRuntime.RegisterSelector(action));
        ((delegate* unmanaged<IntPtr, IntPtr, nint, nint>)ObjCRuntime.MessageSend)(button, SendActionOnSelector, EitherButtonUp);
    }

    private static IntPtr Bar => NSObject.Send(BarClass, SystemBarSelector);
}
