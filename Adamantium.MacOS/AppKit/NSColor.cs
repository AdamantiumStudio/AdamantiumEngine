using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>AppKit's colors.</summary>
public static class NSColor
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSColor");
    private static readonly IntPtr ClearColorSelector = ObjCRuntime.RegisterSelector("clearColor");

    /// <summary>No color at all: a window of it shows only what is drawn in it.</summary>
    public static IntPtr Clear => NSObject.Send(Class, ClearColorSelector);

    private static readonly IntPtr WindowBackgroundColorSelector = ObjCRuntime.RegisterSelector("windowBackgroundColor");

    /// <summary>What an ordinary window is filled with.</summary>
    public static IntPtr WindowBackground => NSObject.Send(Class, WindowBackgroundColorSelector);
}
