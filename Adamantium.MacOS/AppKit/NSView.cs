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

    /// <summary>The window the view is in.</summary>
    public static IntPtr WindowOf(IntPtr view) => NSObject.Send(view, WindowSelector);
}
