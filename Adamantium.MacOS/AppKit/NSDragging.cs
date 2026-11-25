using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>Drags to and from other applications. Main thread only.</summary>
public static unsafe class NSDragging
{
    private static readonly IntPtr ItemClass = ObjCFrameworks.GetClass("NSDraggingItem");
    private static readonly IntPtr RegisterSelector = ObjCRuntime.RegisterSelector("registerForDraggedTypes:");
    private static readonly IntPtr PasteboardSelector = ObjCRuntime.RegisterSelector("draggingPasteboard");
    private static readonly IntPtr SourceSelector = ObjCRuntime.RegisterSelector("draggingSource");
    private static readonly IntPtr SourceMaskSelector = ObjCRuntime.RegisterSelector("draggingSourceOperationMask");
    private static readonly IntPtr InitSelector = ObjCRuntime.RegisterSelector("initWithPasteboardWriter:");
    private static readonly IntPtr SetFrameSelector = ObjCRuntime.RegisterSelector("setDraggingFrame:contents:");
    private static readonly IntPtr BeginSelector =
        ObjCRuntime.RegisterSelector("beginDraggingSessionWithItems:event:source:");
    private static readonly IntPtr ConvertFromScreenSelector = ObjCRuntime.RegisterSelector("convertPointFromScreen:");

    /// <summary>Has the view told of drags that carry data of any of <paramref name="types"/>.</summary>
    public static void Register(IntPtr view, ReadOnlySpan<string> types)
    {
        var nsTypes = new IntPtr[types.Length];
        for (var i = 0; i < types.Length; i++)
            nsTypes[i] = NSString.Create(types[i]);

        NSObject.Send(view, RegisterSelector, NSArray.Create(nsTypes));
        foreach (var type in nsTypes)
            NSObject.Release(type);
    }

    /// <summary>What a drag over a view carries (<see cref="NSPasteboard"/>).</summary>
    public static IntPtr PasteboardOf(IntPtr info) => NSObject.Send(info, PasteboardSelector);

    /// <summary>The view a drag comes from; zero when it comes from another application.</summary>
    public static IntPtr SourceOf(IntPtr info) => NSObject.Send(info, SourceSelector);

    /// <summary>What the source lets the drag do, narrowed by the keys the user holds.</summary>
    public static NSDragOperation OperationsOf(IntPtr info) =>
        (NSDragOperation)NSObject.SendForInteger(info, SourceMaskSelector);

    /// <summary>Starts a drag of <paramref name="items"/> (<see cref="NSPasteboard.CreateItem"/>) from a view, which the
    /// system asks what it may do. <paramref name="mouseEvent"/> is what began it, <paramref name="image"/> an NSImage or
    /// zero, the cursor at its top left corner. False when it does not start.</summary>
    public static bool Begin(IntPtr view, ReadOnlySpan<IntPtr> items, IntPtr mouseEvent, IntPtr image, CGSize size)
    {
        // In the view's own points, as the view fills its window and is not flipped.
        var cursor = ((delegate* unmanaged<IntPtr, IntPtr, CGPoint, CGPoint>)ObjCRuntime.MessageSend)(
            NSView.WindowOf(view), ConvertFromScreenSelector, NSEvent.MouseLocation);
        var frame = new CGRect(cursor.X, cursor.Y - size.Height, size.Width, size.Height);
        var draggingItems = new IntPtr[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            draggingItems[i] = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(
                NSObject.Alloc(ItemClass), InitSelector, items[i]);
            ((delegate* unmanaged<IntPtr, IntPtr, CGRect, IntPtr, void>)ObjCRuntime.MessageSend)(draggingItems[i],
                SetFrameSelector, frame, i == 0 ? image : IntPtr.Zero);
        }

        var session = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(view,
            BeginSelector, NSArray.Create(draggingItems), mouseEvent, view);
        foreach (var item in draggingItems)
            NSObject.Release(item);

        return session != IntPtr.Zero;
    }
}
