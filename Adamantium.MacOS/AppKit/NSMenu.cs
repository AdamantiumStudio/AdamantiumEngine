using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>Menus of the menu bar. An item sends its action to whatever in the application answers it.</summary>
public static unsafe class NSMenu
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSMenu");
    private static readonly IntPtr ItemClass = ObjCFrameworks.GetClass("NSMenuItem");
    private static readonly IntPtr InitWithTitleSelector = ObjCRuntime.RegisterSelector("initWithTitle:");
    private static readonly IntPtr InitItemSelector = ObjCRuntime.RegisterSelector("initWithTitle:action:keyEquivalent:");
    private static readonly IntPtr SeparatorSelector = ObjCRuntime.RegisterSelector("separatorItem");
    private static readonly IntPtr AddItemSelector = ObjCRuntime.RegisterSelector("addItem:");
    private static readonly IntPtr SetTargetSelector = ObjCRuntime.RegisterSelector("setTarget:");
    private static readonly IntPtr SetTagSelector = ObjCRuntime.RegisterSelector("setTag:");
    private static readonly IntPtr TagSelector = ObjCRuntime.RegisterSelector("tag");
    private static readonly IntPtr SetStateSelector = ObjCRuntime.RegisterSelector("setState:");
    private static readonly IntPtr PopUpSelector =
        ObjCRuntime.RegisterSelector("popUpMenuPositioningItem:atLocation:inView:");
    private static readonly IntPtr SetSubmenuSelector = ObjCRuntime.RegisterSelector("setSubmenu:");
    private static readonly IntPtr SetModifierMaskSelector = ObjCRuntime.RegisterSelector("setKeyEquivalentModifierMask:");

    /// <summary>A new menu, owned by the caller.</summary>
    public static IntPtr Create(string title)
    {
        var nsTitle = NSString.Create(title);
        var menu = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(NSObject.Alloc(Class),
            InitWithTitleSelector, nsTitle);
        NSObject.Release(nsTitle);
        return menu;
    }

    /// <summary>Adds an item that sends <paramref name="action"/> (an Objective-C selector); <paramref name="key"/> with
    /// <paramref name="modifiers"/> does it too, empty for none.</summary>
    public static void AddItem(IntPtr menu, string title, string action, string key = "",
        NSEventModifierFlags modifiers = NSEventModifierFlags.Command)
    {
        var item = CreateItem(title, ObjCRuntime.RegisterSelector(action), key);
        ((delegate* unmanaged<IntPtr, IntPtr, ulong, void>)ObjCRuntime.MessageSend)(item, SetModifierMaskSelector,
            (ulong)modifiers);
        NSObject.Send(menu, AddItemSelector, item);
        NSObject.Release(item);
    }

    /// <summary>Adds an item that sends <paramref name="action"/> to <paramref name="target"/>, not retained, with
    /// <paramref name="tag"/> to tell it by (<see cref="TagOf"/>).</summary>
    public static void AddItem(IntPtr menu, string title, IntPtr target, string action, nint tag, bool isEnabled,
        bool isChecked)
    {
        var item = CreateItem(title, isEnabled ? ObjCRuntime.RegisterSelector(action) : IntPtr.Zero, string.Empty);
        NSObject.Send(item, SetTargetSelector, target);
        SendInteger(item, SetTagSelector, tag);
        SendInteger(item, SetStateSelector, isChecked ? 1 : 0);
        NSObject.Send(menu, AddItemSelector, item);
        NSObject.Release(item);
    }

    /// <summary>What an item is told by.</summary>
    public static nint TagOf(IntPtr item) => NSObject.SendForInteger(item, TagSelector);

    /// <summary>Opens the menu under <paramref name="view"/> and waits until it closes.</summary>
    public static void PopUp(IntPtr menu, IntPtr view)
    {
        var below = NSView.BoundsOf(view).Size.Height;
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, CGPoint, IntPtr, byte>)ObjCRuntime.MessageSend)(menu, PopUpSelector,
            IntPtr.Zero, new CGPoint(0, below), view);
    }

    private static void SendInteger(IntPtr receiver, IntPtr selector, nint value) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, void>)ObjCRuntime.MessageSend)(receiver, selector, value);

    /// <summary>Adds a line between items.</summary>
    public static void AddSeparator(IntPtr menu) =>
        NSObject.Send(menu, AddItemSelector, NSObject.Send(ItemClass, SeparatorSelector));

    /// <summary>Adds an item that opens <paramref name="submenu"/>, which it retains.</summary>
    public static void AddSubmenu(IntPtr menu, string title, IntPtr submenu)
    {
        var item = CreateItem(title, IntPtr.Zero, string.Empty);
        NSObject.Send(item, SetSubmenuSelector, submenu);
        NSObject.Send(menu, AddItemSelector, item);
        NSObject.Release(item);
    }

    private static IntPtr CreateItem(string title, IntPtr action, string key)
    {
        var nsTitle = NSString.Create(title);
        var nsKey = NSString.Create(key);
        var item = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(ItemClass), InitItemSelector, nsTitle, action, nsKey);
        NSObject.Release(nsTitle);
        NSObject.Release(nsKey);
        return item;
    }
}
