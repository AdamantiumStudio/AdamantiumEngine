using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>A view: made with a frame, backed by a layer, measured in points.</summary>
public static unsafe class NSView
{
    private static readonly IntPtr InitWithFrameSelector = ObjCRuntime.RegisterSelector("initWithFrame:");
    private static readonly IntPtr SetWantsLayerSelector = ObjCRuntime.RegisterSelector("setWantsLayer:");
    private static readonly IntPtr SetLayerSelector = ObjCRuntime.RegisterSelector("setLayer:");
    private static readonly IntPtr LayerSelector = ObjCRuntime.RegisterSelector("layer");
    private static readonly IntPtr BoundsSelector = ObjCRuntime.RegisterSelector("bounds");
    private static readonly IntPtr WindowSelector = ObjCRuntime.RegisterSelector("window");

    /// <summary>A new view of <paramref name="viewClass"/> (NSView or a subclass) with <paramref name="frame"/>, owned
    /// by the caller.</summary>
    public static IntPtr Create(IntPtr viewClass, CGRect frame) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, IntPtr>)ObjCRuntime.MessageSend)(NSObject.Alloc(viewClass),
            InitWithFrameSelector, frame);

    /// <summary>Backs the view with <paramref name="layer"/>, which it then hosts and sizes.</summary>
    public static void SetLayer(IntPtr view, IntPtr layer)
    {
        NSObject.Send(view, SetLayerSelector, layer);
        NSObject.Send(view, SetWantsLayerSelector, true);
    }

    /// <summary>The layer backing the view.</summary>
    public static IntPtr LayerOf(IntPtr view) => NSObject.Send(view, LayerSelector);

    /// <summary>The view's own rectangle, in points.</summary>
    public static CGRect BoundsOf(IntPtr view) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect>)ObjCRuntime.MessageSend)(view, BoundsSelector);

    private static readonly IntPtr AddTrackingAreaSelector = ObjCRuntime.RegisterSelector("addTrackingArea:");
    private static readonly IntPtr InitTrackingAreaSelector =
        ObjCRuntime.RegisterSelector("initWithRect:options:owner:userInfo:");

    // Entered and exited, moved, whether or not the application is active, over what is visible of the view.
    private const ulong TrackingOptions = 0x01 | 0x02 | 0x80 | 0x200;

    /// <summary>Has the view told of the mouse moving over it and leaving it, in any window.</summary>
    public static void TrackMouse(IntPtr view)
    {
        var area = ((delegate* unmanaged<IntPtr, IntPtr, CGRect, ulong, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(ObjCRuntime.GetClass("NSTrackingArea")), InitTrackingAreaSelector, default, TrackingOptions,
            view, IntPtr.Zero);
        NSObject.Send(view, AddTrackingAreaSelector, area);
        NSObject.Release(area);
    }

    /// <summary>The window the view is in.</summary>
    public static IntPtr WindowOf(IntPtr view) => NSObject.Send(view, WindowSelector);
}
