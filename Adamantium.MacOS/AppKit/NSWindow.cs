using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>A window: made, shown, hidden, moved and closed, with its content view, delegate, title and the scale of
/// the screen it is on.</summary>
public static unsafe class NSWindow
{
    private const ulong BufferedBacking = 2;

    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSWindow");
    private static readonly IntPtr InitSelector =
        ObjCRuntime.RegisterSelector("initWithContentRect:styleMask:backing:defer:");
    private static readonly IntPtr SetReleasedWhenClosedSelector = ObjCRuntime.RegisterSelector("setReleasedWhenClosed:");
    private static readonly IntPtr SetAcceptsMouseMovedEventsSelector =
        ObjCRuntime.RegisterSelector("setAcceptsMouseMovedEvents:");
    private static readonly IntPtr SetTitleSelector = ObjCRuntime.RegisterSelector("setTitle:");
    private static readonly IntPtr SetContentViewSelector = ObjCRuntime.RegisterSelector("setContentView:");
    private static readonly IntPtr ContentViewSelector = ObjCRuntime.RegisterSelector("contentView");
    private static readonly IntPtr SetDelegateSelector = ObjCRuntime.RegisterSelector("setDelegate:");
    private static readonly IntPtr MakeKeyAndOrderFrontSelector = ObjCRuntime.RegisterSelector("makeKeyAndOrderFront:");
    private static readonly IntPtr OrderFrontSelector = ObjCRuntime.RegisterSelector("orderFront:");
    private static readonly IntPtr OrderOutSelector = ObjCRuntime.RegisterSelector("orderOut:");
    private static readonly IntPtr CloseSelector = ObjCRuntime.RegisterSelector("close");
    private static readonly IntPtr MiniaturizeSelector = ObjCRuntime.RegisterSelector("miniaturize:");
    private static readonly IntPtr DeminiaturizeSelector = ObjCRuntime.RegisterSelector("deminiaturize:");
    private static readonly IntPtr IsMiniaturizedSelector = ObjCRuntime.RegisterSelector("isMiniaturized");
    private static readonly IntPtr ZoomSelector = ObjCRuntime.RegisterSelector("zoom:");
    private static readonly IntPtr IsZoomedSelector = ObjCRuntime.RegisterSelector("isZoomed");
    private static readonly IntPtr IsKeyWindowSelector = ObjCRuntime.RegisterSelector("isKeyWindow");
    private static readonly IntPtr BackingScaleFactorSelector = ObjCRuntime.RegisterSelector("backingScaleFactor");
    private static readonly IntPtr FrameSelector = ObjCRuntime.RegisterSelector("frame");
    private static readonly IntPtr SetFrameSelector = ObjCRuntime.RegisterSelector("setFrame:display:");
    private static readonly IntPtr ContentRectForFrameSelector = ObjCRuntime.RegisterSelector("contentRectForFrameRect:");
    private static readonly IntPtr FrameRectForContentSelector = ObjCRuntime.RegisterSelector("frameRectForContentRect:");
    private static readonly IntPtr ScreenSelector = ObjCRuntime.RegisterSelector("screen");
    private static readonly IntPtr WindowNumberSelector = ObjCRuntime.RegisterSelector("windowNumber");
    private static readonly IntPtr MakeFirstResponderSelector = ObjCRuntime.RegisterSelector("makeFirstResponder:");

    /// <summary>A new window whose content is <paramref name="contentRect"/> in screen points, owned by the caller
    /// and kept after <see cref="Close"/> until released.</summary>
    public static IntPtr Create(CGRect contentRect, NSWindowStyleMask style)
    {
        var window = ((delegate* unmanaged<IntPtr, IntPtr, CGRect, ulong, ulong, byte, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(Class), InitSelector, contentRect, (ulong)style, BufferedBacking, 0);
        NSObject.Send(window, SetReleasedWhenClosedSelector, false);
        NSObject.Send(window, SetAcceptsMouseMovedEventsSelector, true);
        return window;
    }

    /// <summary>Sets the title shown in the title bar.</summary>
    public static void SetTitle(IntPtr window, string title)
    {
        var text = NSString.Create(title);
        NSObject.Send(window, SetTitleSelector, text);
        NSObject.Release(text);
    }

    /// <summary>Sets the view that fills the window.</summary>
    public static void SetContentView(IntPtr window, IntPtr view) => NSObject.Send(window, SetContentViewSelector, view);

    /// <summary>The view that fills the window.</summary>
    public static IntPtr ContentViewOf(IntPtr window) => NSObject.Send(window, ContentViewSelector);

    /// <summary>Sets the window's delegate (not retained by the window).</summary>
    public static void SetDelegate(IntPtr window, IntPtr windowDelegate) =>
        NSObject.Send(window, SetDelegateSelector, windowDelegate);

    /// <summary>Shows the window in front and gives it the keyboard.</summary>
    public static void MakeKeyAndOrderFront(IntPtr window) =>
        NSObject.Send(window, MakeKeyAndOrderFrontSelector, IntPtr.Zero);

    /// <summary>Shows the window in front without giving it the keyboard.</summary>
    public static void OrderFront(IntPtr window) => NSObject.Send(window, OrderFrontSelector, IntPtr.Zero);

    /// <summary>Hides the window.</summary>
    public static void OrderOut(IntPtr window) => NSObject.Send(window, OrderOutSelector, IntPtr.Zero);

    /// <summary>Closes the window.</summary>
    public static void Close(IntPtr window) => NSObject.Send(window, CloseSelector);

    /// <summary>Minimizes the window to the Dock.</summary>
    public static void Miniaturize(IntPtr window) => NSObject.Send(window, MiniaturizeSelector, IntPtr.Zero);

    /// <summary>Brings the window back from the Dock.</summary>
    public static void Deminiaturize(IntPtr window) => NSObject.Send(window, DeminiaturizeSelector, IntPtr.Zero);

    /// <summary>Whether the window is in the Dock.</summary>
    public static bool IsMiniaturized(IntPtr window) => NSObject.SendForBoolean(window, IsMiniaturizedSelector);

    /// <summary>Zooms the window to fill its screen, or back.</summary>
    public static void Zoom(IntPtr window) => NSObject.Send(window, ZoomSelector, IntPtr.Zero);

    /// <summary>Whether the window is zoomed.</summary>
    public static bool IsZoomed(IntPtr window) => NSObject.SendForBoolean(window, IsZoomedSelector);

    /// <summary>Whether the window has the keyboard.</summary>
    public static bool IsKeyWindow(IntPtr window) => NSObject.SendForBoolean(window, IsKeyWindowSelector);

    /// <summary>Device pixels per point on the window's screen: 2 on a Retina display.</summary>
    public static double BackingScaleFactorOf(IntPtr window) => NSObject.SendForDouble(window, BackingScaleFactorSelector);

    /// <summary>The window's frame, title bar included, in screen points from the bottom-left of the main screen.
    /// </summary>
    public static CGRect FrameOf(IntPtr window) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect>)ObjCRuntime.MessageSend)(window, FrameSelector);

    /// <summary>Moves and sizes the window's frame.</summary>
    public static void SetFrame(IntPtr window, CGRect frame) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, byte, void>)ObjCRuntime.MessageSend)(window, SetFrameSelector,
            frame, 1);

    /// <summary>The content area of a frame of this window.</summary>
    public static CGRect ContentRectForFrame(IntPtr window, CGRect frame) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, CGRect>)ObjCRuntime.MessageSend)(window, ContentRectForFrameSelector,
            frame);

    /// <summary>The frame of this window around a content area.</summary>
    public static CGRect FrameRectForContent(IntPtr window, CGRect content) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, CGRect>)ObjCRuntime.MessageSend)(window, FrameRectForContentSelector,
            content);

    /// <summary>The NSScreen the window is mostly on; zero when it is on none.</summary>
    public static IntPtr ScreenOf(IntPtr window) => NSObject.Send(window, ScreenSelector);

    /// <summary>The window server's number for the window.</summary>
    public static nint NumberOf(IntPtr window) => NSObject.SendForInteger(window, WindowNumberSelector);

    /// <summary>Gives the keyboard to <paramref name="view"/>.</summary>
    public static void MakeFirstResponder(IntPtr window, IntPtr view) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(window, MakeFirstResponderSelector,
            view);
}
