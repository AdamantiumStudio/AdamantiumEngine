using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>A button that opens a list and shows what was chosen of it. Main thread only.</summary>
public static unsafe class NSPopUpButton
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSPopUpButton");
    private static readonly IntPtr InitSelector = ObjCRuntime.RegisterSelector("initWithFrame:pullsDown:");
    private static readonly IntPtr MenuSelector = ObjCRuntime.RegisterSelector("menu");
    private static readonly IntPtr SelectSelector = ObjCRuntime.RegisterSelector("selectItemAtIndex:");
    private static readonly IntPtr SelectedSelector = ObjCRuntime.RegisterSelector("indexOfSelectedItem");
    private static readonly IntPtr SizeToFitSelector = ObjCRuntime.RegisterSelector("sizeToFit");

    /// <summary>A new button with nothing in its list, owned by the caller.</summary>
    public static IntPtr Create() =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, byte, IntPtr>)ObjCRuntime.MessageSend)(NSObject.Alloc(Class),
            InitSelector, default, 0);

    /// <summary>The menu of the list (<see cref="NSMenu"/>).</summary>
    public static IntPtr MenuOf(IntPtr button) => NSObject.Send(button, MenuSelector);

    /// <summary>Chooses an item, without its action.</summary>
    public static void Select(IntPtr button, nint index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, void>)ObjCRuntime.MessageSend)(button, SelectSelector, index);

    /// <summary>Which item is chosen; -1 for none.</summary>
    public static nint SelectedOf(IntPtr button) => NSObject.SendForInteger(button, SelectedSelector);

    /// <summary>Makes it as large as its widest item.</summary>
    public static void SizeToFit(IntPtr button) =>
        ((delegate* unmanaged<IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(button, SizeToFitSelector);
}
