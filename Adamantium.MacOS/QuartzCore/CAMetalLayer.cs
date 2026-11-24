using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.QuartzCore;

/// <summary>The layer Vulkan draws into on Apple's platforms (VK_EXT_metal_surface): its drawable size is in device
/// pixels, its contents scale the pixels per point.</summary>
public static unsafe class CAMetalLayer
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("CAMetalLayer");
    private static readonly IntPtr LayerSelector = ObjCRuntime.RegisterSelector("layer");
    private static readonly IntPtr SetContentsScaleSelector = ObjCRuntime.RegisterSelector("setContentsScale:");
    private static readonly IntPtr SetDrawableSizeSelector = ObjCRuntime.RegisterSelector("setDrawableSize:");

    /// <summary>A new CAMetalLayer, autoreleased: a view that hosts it keeps it.</summary>
    public static IntPtr Create() => NSObject.Send(Class, LayerSelector);

    /// <summary>Sets the pixels per point the layer draws at.</summary>
    public static void SetContentsScale(IntPtr layer, double scale) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, void>)ObjCRuntime.MessageSend)(layer, SetContentsScaleSelector, scale);

    /// <summary>Sets the size of the layer's drawables, in device pixels.</summary>
    public static void SetDrawableSize(IntPtr layer, CGSize size) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGSize, void>)ObjCRuntime.MessageSend)(layer, SetDrawableSizeSelector, size);
}
