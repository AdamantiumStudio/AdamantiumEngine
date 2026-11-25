using System;
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
