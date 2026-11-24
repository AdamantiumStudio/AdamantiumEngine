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
    private const ulong FullScreenPrimary = 1 << 7;
    private const ulong FullScreenNone = 1 << 9;

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
    private static readonly IntPtr SetOpaqueSelector = ObjCRuntime.RegisterSelector("setOpaque:");
    private static readonly IntPtr SetBackgroundColorSelector = ObjCRuntime.RegisterSelector("setBackgroundColor:");
    private static readonly IntPtr SetIgnoresMouseEventsSelector = ObjCRuntime.RegisterSelector("setIgnoresMouseEvents:");
    private static readonly IntPtr SetHasShadowSelector = ObjCRuntime.RegisterSelector("setHasShadow:");
    private static readonly IntPtr SetLevelSelector = ObjCRuntime.RegisterSelector("setLevel:");
    private static readonly IntPtr SetTitlebarAppearsTransparentSelector =
        ObjCRuntime.RegisterSelector("setTitlebarAppearsTransparent:");
    private static readonly IntPtr SetTitleVisibilitySelector = ObjCRuntime.RegisterSelector("setTitleVisibility:");
    private static readonly IntPtr StandardWindowButtonSelector = ObjCRuntime.RegisterSelector("standardWindowButton:");
    private static readonly IntPtr SetHiddenSelector = ObjCRuntime.RegisterSelector("setHidden:");
    private static readonly IntPtr PerformWindowDragSelector = ObjCRuntime.RegisterSelector("performWindowDragWithEvent:");
    private static readonly IntPtr SetStyleMaskSelector = ObjCRuntime.RegisterSelector("setStyleMask:");
    private static readonly IntPtr StyleMaskSelector = ObjCRuntime.RegisterSelector("styleMask");
    private static readonly IntPtr SetCollectionBehaviorSelector = ObjCRuntime.RegisterSelector("setCollectionBehavior:");
    private static readonly IntPtr ToggleFullScreenSelector = ObjCRuntime.RegisterSelector("toggleFullScreen:");
    private static readonly IntPtr IsVisibleSelector = ObjCRuntime.RegisterSelector("isVisible");
    private static readonly IntPtr SetContentMinSizeSelector = ObjCRuntime.RegisterSelector("setContentMinSize:");
    private static readonly IntPtr SetContentMaxSizeSelector = ObjCRuntime.RegisterSelector("setContentMaxSize:");
    private static readonly IntPtr SetAlphaValueSelector = ObjCRuntime.RegisterSelector("setAlphaValue:");
    private static readonly IntPtr AddChildWindowSelector = ObjCRuntime.RegisterSelector("addChildWindow:ordered:");
    private static readonly IntPtr RemoveChildWindowSelector = ObjCRuntime.RegisterSelector("removeChildWindow:");

    /// <summary>NSNormalWindowLevel: where ordinary windows are.</summary>
    public const nint NormalLevel = 0;

    /// <summary>NSFloatingWindowLevel: above the ordinary windows, as a palette is.</summary>
    public const nint FloatingLevel = 3;

    /// <summary>NSPopUpMenuWindowLevel: above the windows of every application, as a drag's image is.</summary>
    public const nint PopUpMenuLevel = 101;

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

    /// <summary>Makes the window see-through where nothing is drawn: not opaque, of no background color, without a
    /// shadow.</summary>
    public static void MakeTransparent(IntPtr window)
    {
        NSObject.Send(window, SetOpaqueSelector, false);
        NSObject.Send(window, SetBackgroundColorSelector, NSColor.Clear);
        NSObject.Send(window, SetHasShadowSelector, false);
    }

    /// <summary>Hands the title bar to the content: a window made with <see cref="NSWindowStyleMask.FullSizeContentView"/>
    /// keeps its frame, shadow and resizing, while its title bar shows no title, no buttons and nothing of its own.
    /// </summary>
    public static void HideTitleBar(IntPtr window)
    {
        NSObject.Send(window, SetTitlebarAppearsTransparentSelector, true);
        ((delegate* unmanaged<IntPtr, IntPtr, nint, void>)ObjCRuntime.MessageSend)(window, SetTitleVisibilitySelector, 1);
        for (nint button = 0; button <= 2; button++)
        {
            var standard = ((delegate* unmanaged<IntPtr, IntPtr, nint, IntPtr>)ObjCRuntime.MessageSend)(window,
                StandardWindowButtonSelector, button);
            if (standard != IntPtr.Zero)
                NSObject.Send(standard, SetHiddenSelector, true);
        }
    }

    /// <summary>Moves the window with the mouse from the press <paramref name="mouseDown"/>, as dragging its title bar
    /// would - while the button is still held.</summary>
    public static void PerformWindowDrag(IntPtr window, IntPtr mouseDown) =>
        NSObject.Send(window, PerformWindowDragSelector, mouseDown);

    /// <summary>Lets the mouse through the window to what is below it.</summary>
    public static void SetIgnoresMouseEvents(IntPtr window, bool ignores) =>
        NSObject.Send(window, SetIgnoresMouseEventsSelector, ignores);

    /// <summary>Sets how high the window floats among others (NSWindowLevel).</summary>
    public static void SetLevel(IntPtr window, nint level) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint, void>)ObjCRuntime.MessageSend)(window, SetLevelSelector, level);

    /// <summary>Gives the keyboard to <paramref name="view"/>.</summary>
    public static void MakeFirstResponder(IntPtr window, IntPtr view) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(window, MakeFirstResponderSelector,
            view);

    /// <summary>What the window has now: title bar, buttons, resizing, full screen.</summary>
    public static NSWindowStyleMask StyleMaskOf(IntPtr window) =>
        (NSWindowStyleMask)(ulong)NSObject.SendForInteger(window, StyleMaskSelector);

    /// <summary>Sets whether the window may have a full screen of its own.</summary>
    public static void SetAllowsFullScreen(IntPtr window, bool allows) =>
        ((delegate* unmanaged<IntPtr, IntPtr, ulong, void>)ObjCRuntime.MessageSend)(window,
            SetCollectionBehaviorSelector, allows ? FullScreenPrimary : FullScreenNone);

    /// <summary>Takes the window to a full screen of its own, or back.</summary>
    public static void ToggleFullScreen(IntPtr window) => NSObject.Send(window, ToggleFullScreenSelector, IntPtr.Zero);

    /// <summary>Whether the window is on screen, even behind others.</summary>
    public static bool IsVisible(IntPtr window) => NSObject.SendForBoolean(window, IsVisibleSelector);

    /// <summary>Changes what the window has: title bar, buttons, resizing.</summary>
    public static void SetStyleMask(IntPtr window, NSWindowStyleMask style) =>
        ((delegate* unmanaged<IntPtr, IntPtr, ulong, void>)ObjCRuntime.MessageSend)(window, SetStyleMaskSelector,
            (ulong)style);

    /// <summary>The smallest size, in points, the user can resize the content to.</summary>
    public static void SetContentMinSize(IntPtr window, CGSize size) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGSize, void>)ObjCRuntime.MessageSend)(window, SetContentMinSizeSelector,
            size);

    /// <summary>The largest size, in points, the user can resize the content to.</summary>
    public static void SetContentMaxSize(IntPtr window, CGSize size) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGSize, void>)ObjCRuntime.MessageSend)(window, SetContentMaxSizeSelector,
            size);

    /// <summary>How opaque the whole window is, from 0 to 1.</summary>
    public static void SetAlphaValue(IntPtr window, double alpha) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, void>)ObjCRuntime.MessageSend)(window, SetAlphaValueSelector, alpha);

    /// <summary>Makes <paramref name="child"/> stay above the window and move with it.</summary>
    public static void AddChildWindow(IntPtr window, IntPtr child) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint, void>)ObjCRuntime.MessageSend)(window, AddChildWindowSelector,
            child, 1);

    /// <summary>Lets <paramref name="child"/> go its own way again.</summary>
    public static void RemoveChildWindow(IntPtr window, IntPtr child) =>
        NSObject.Send(window, RemoveChildWindowSelector, child);
}
