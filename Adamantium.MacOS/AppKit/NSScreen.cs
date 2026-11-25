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

    private static readonly IntPtr ScreensSelector = ObjCRuntime.RegisterSelector("screens");
    private static readonly IntPtr DeviceDescriptionSelector = ObjCRuntime.RegisterSelector("deviceDescription");
    private static readonly IntPtr ObjectForKeySelector = ObjCRuntime.RegisterSelector("objectForKey:");
    private static readonly IntPtr IntegerValueSelector = ObjCRuntime.RegisterSelector("integerValue");

    /// <summary>Posted when displays are connected, disconnected or rearranged.</summary>
    public const string ParametersChangedNotification = "NSApplicationDidChangeScreenParametersNotification";

    /// <summary>The connected screens; the first is the one with the menu bar, at whose corner coordinates start.</summary>
    public static IntPtr[] All
    {
        get
        {
            var screens = NSObject.Send(Class, ScreensSelector);
            var all = new IntPtr[NSArray.CountOf(screens)];
            for (var i = 0; i < all.Length; i++)
                all[i] = NSArray.ObjectAt(screens, i);

            return all;
        }
    }

    /// <summary>The CGDirectDisplayID of the screen, good while it stays connected.</summary>
    public static uint DisplayIdOf(IntPtr screen)
    {
        var key = NSString.Create("NSScreenNumber");
        var number = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Send(screen, DeviceDescriptionSelector), ObjectForKeySelector, key);
        NSObject.Release(key);
        return number == IntPtr.Zero ? 0 : (uint)NSObject.SendForInteger(number, IntegerValueSelector);
    }

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
