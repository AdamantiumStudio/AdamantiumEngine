using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>A display: its frame in points, its scale and how many frames a second it shows.</summary>
public static unsafe class NSScreen
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSScreen");
    private static readonly IntPtr MainScreenSelector = ObjCRuntime.RegisterSelector("mainScreen");
    private static readonly IntPtr FrameSelector = ObjCRuntime.RegisterSelector("frame");
    private static readonly IntPtr VisibleFrameSelector = ObjCRuntime.RegisterSelector("visibleFrame");
    private static readonly IntPtr BackingScaleFactorSelector = ObjCRuntime.RegisterSelector("backingScaleFactor");
    private static readonly IntPtr MaximumFramesPerSecondSelector =
        ObjCRuntime.RegisterSelector("maximumFramesPerSecond");

    /// <summary>The screen with the window that has the keyboard, else the one with the menu bar.</summary>
    public static IntPtr Main => NSObject.Send(Class, MainScreenSelector);

    /// <summary>The screen's frame, in points from the bottom-left of the screen with the menu bar.</summary>
    public static CGRect FrameOf(IntPtr screen) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect>)ObjCRuntime.MessageSend)(screen, FrameSelector);

    /// <summary>The frame less the menu bar and the Dock.</summary>
    public static CGRect VisibleFrameOf(IntPtr screen) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect>)ObjCRuntime.MessageSend)(screen, VisibleFrameSelector);

    /// <summary>Device pixels per point.</summary>
    public static double BackingScaleFactorOf(IntPtr screen) => NSObject.SendForDouble(screen, BackingScaleFactorSelector);

    /// <summary>The most frames a second the screen shows.</summary>
    public static nint MaximumFramesPerSecondOf(IntPtr screen) =>
        NSObject.SendForInteger(screen, MaximumFramesPerSecondSelector);
}
