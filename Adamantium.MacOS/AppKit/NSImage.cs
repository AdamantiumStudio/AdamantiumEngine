using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>AppKit images, made from Core Graphics ones.</summary>
public static unsafe class NSImage
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSImage");
    private static readonly IntPtr InitWithCGImageSelector = ObjCRuntime.RegisterSelector("initWithCGImage:size:");

    /// <summary>A new NSImage of <paramref name="cgImage"/> at <paramref name="size"/> points, owned by the caller.
    /// </summary>
    public static IntPtr FromCGImage(IntPtr cgImage, CGSize size) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, CGSize, IntPtr>)ObjCRuntime.MessageSend)(NSObject.Alloc(Class),
            InitWithCGImageSelector, cgImage, size);
}
