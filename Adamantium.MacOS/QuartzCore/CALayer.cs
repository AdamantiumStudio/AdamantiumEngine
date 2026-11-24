using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.QuartzCore;

/// <summary>What a Core Animation layer shows.</summary>
public static unsafe class CALayer
{
    private static readonly IntPtr SetContentsSelector = ObjCRuntime.RegisterSelector("setContents:");
    private static readonly IntPtr SetContentsScaleSelector = ObjCRuntime.RegisterSelector("setContentsScale:");

    /// <summary>Shows <paramref name="image"/> (a CGImage, which the layer retains) stretched over the layer.</summary>
    public static void SetContents(IntPtr layer, IntPtr image) => NSObject.Send(layer, SetContentsSelector, image);

    /// <summary>Sets the pixels per point of the layer's contents.</summary>
    public static void SetContentsScale(IntPtr layer, double scale) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, void>)ObjCRuntime.MessageSend)(layer, SetContentsScaleSelector, scale);
}
