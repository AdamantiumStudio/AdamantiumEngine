using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.QuartzCore;

/// <summary>What a Core Animation layer shows.</summary>
public static unsafe class CALayer
{
    private static readonly IntPtr SetContentsSelector = ObjCRuntime.RegisterSelector("setContents:");
    private static readonly IntPtr SetContentsScaleSelector = ObjCRuntime.RegisterSelector("setContentsScale:");
    private static readonly IntPtr SetBoundsSelector = ObjCRuntime.RegisterSelector("setBounds:");

    /// <summary>Shows <paramref name="image"/> (a CGImage, which the layer retains) stretched over the layer.</summary>
    public static void SetContents(IntPtr layer, IntPtr image) => NSObject.Send(layer, SetContentsSelector, image);

    /// <summary>Sets the pixels per point of the layer's contents.</summary>
    public static void SetContentsScale(IntPtr layer, double scale) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, void>)ObjCRuntime.MessageSend)(layer, SetContentsScaleSelector, scale);

    /// <summary>Sets the layer's own rectangle, in points. A view that hosts a layer sets it.</summary>
    public static void SetBounds(IntPtr layer, CGRect bounds) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, void>)ObjCRuntime.MessageSend)(layer, SetBoundsSelector, bounds);
}
