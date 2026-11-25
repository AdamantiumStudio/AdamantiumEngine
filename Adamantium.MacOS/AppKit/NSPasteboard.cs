using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>The clipboard, and others like it: a list of items, each with data of several types.</summary>
public static unsafe class NSPasteboard
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSPasteboard");
    private static readonly IntPtr ItemClass = ObjCFrameworks.GetClass("NSPasteboardItem");
    private static readonly IntPtr GeneralSelector = ObjCRuntime.RegisterSelector("generalPasteboard");
    private static readonly IntPtr UniqueSelector = ObjCRuntime.RegisterSelector("pasteboardWithUniqueName");
    private static readonly IntPtr ReleaseGloballySelector = ObjCRuntime.RegisterSelector("releaseGlobally");
    private static readonly IntPtr ClearSelector = ObjCRuntime.RegisterSelector("clearContents");
    private static readonly IntPtr WriteSelector = ObjCRuntime.RegisterSelector("writeObjects:");
    private static readonly IntPtr ItemsSelector = ObjCRuntime.RegisterSelector("pasteboardItems");
    private static readonly IntPtr DataForTypeSelector = ObjCRuntime.RegisterSelector("dataForType:");
    private static readonly IntPtr SetDataSelector = ObjCRuntime.RegisterSelector("setData:forType:");
    private static readonly IntPtr SetProviderSelector = ObjCRuntime.RegisterSelector("setDataProvider:forTypes:");
    private static readonly IntPtr AvailableTypeSelector = ObjCRuntime.RegisterSelector("availableTypeFromArray:");

    /// <summary>The clipboard; the system's, not retained.</summary>
    public static IntPtr General => NSObject.Send(Class, GeneralSelector);

    /// <summary>A pasteboard of its own, owned by the caller: <see cref="ReleaseGlobally"/> when done.</summary>
    public static IntPtr CreateUnique() => NSObject.Retain(NSObject.Send(Class, UniqueSelector));

    /// <summary>Does away with a pasteboard made by <see cref="CreateUnique"/>.</summary>
    public static void ReleaseGlobally(IntPtr pasteboard)
    {
        NSObject.Send(pasteboard, ReleaseGloballySelector);
        NSObject.Release(pasteboard);
    }

    /// <summary>A new item, owned by the caller.</summary>
    public static IntPtr CreateItem() => NSObject.New(ItemClass);

    /// <summary>Gives an item its data of a type.</summary>
    public static void SetData(IntPtr item, ReadOnlySpan<byte> bytes, string type)
    {
        var nsType = NSString.Create(type);
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(item, SetDataSelector,
            NSData.Create(bytes), nsType);
        NSObject.Release(nsType);
    }

    /// <summary>Has an item ask <paramref name="provider"/> (NSPasteboardItemDataProvider), retained until the item is done
    /// with, for its data of some types when they are wanted.</summary>
    public static void SetProvider(IntPtr item, IntPtr provider, ReadOnlySpan<string> types)
    {
        var nsTypes = new IntPtr[types.Length];
        for (var i = 0; i < types.Length; i++)
            nsTypes[i] = NSString.Create(types[i]);

        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(item, SetProviderSelector,
            provider, NSArray.Create(nsTypes));
        foreach (var type in nsTypes)
            NSObject.Release(type);
    }

    /// <summary>Puts <paramref name="items"/> on the pasteboard in place of what was there; false when it did not take
    /// them.</summary>
    public static bool Write(IntPtr pasteboard, ReadOnlySpan<IntPtr> items)
    {
        NSObject.SendForInteger(pasteboard, ClearSelector);
        return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(pasteboard, WriteSelector,
            NSArray.Create(items)) != 0;
    }

    /// <summary>The items on the pasteboard, owned by it; an NSArray, null when it cannot be read.</summary>
    public static IntPtr ItemsOf(IntPtr pasteboard) => NSObject.Send(pasteboard, ItemsSelector);

    /// <summary>Whether the pasteboard has data of a type, without reading it.</summary>
    public static bool Contains(IntPtr pasteboard, string type)
    {
        var nsType = NSString.Create(type);
        var types = NSArray.Create([nsType]);
        NSObject.Release(nsType);
        return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(pasteboard,
            AvailableTypeSelector, types) != IntPtr.Zero;
    }

    /// <summary>The data of a type on a pasteboard, the first item's that has it, or of one item; null when there is none.
    /// </summary>
    public static byte[] DataOf(IntPtr pasteboardOrItem, string type)
    {
        var nsType = NSString.Create(type);
        var data = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(pasteboardOrItem,
            DataForTypeSelector, nsType);
        NSObject.Release(nsType);
        return NSData.ToManaged(data);
    }
}
