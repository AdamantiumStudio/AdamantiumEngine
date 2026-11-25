using System;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>The system's cursors, setting one, and hiding the cursor (each <see cref="Hide"/> wants its
/// <see cref="Unhide"/>).</summary>
public static class NSCursor
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSCursor");
    private static readonly IntPtr SetSelector = ObjCRuntime.RegisterSelector("set");
    private static readonly IntPtr HideSelector = ObjCRuntime.RegisterSelector("hide");
    private static readonly IntPtr UnhideSelector = ObjCRuntime.RegisterSelector("unhide");

    /// <summary>The arrow.</summary>
    public static IntPtr Arrow => Named("arrowCursor");

    /// <summary>The text cursor.</summary>
    public static IntPtr IBeam => Named("IBeamCursor");

    /// <summary>The pointing hand of a link.</summary>
    public static IntPtr PointingHand => Named("pointingHandCursor");

    /// <summary>The closed hand of something dragged.</summary>
    public static IntPtr ClosedHand => Named("closedHandCursor");

    /// <summary>The open hand of something that can be dragged.</summary>
    public static IntPtr OpenHand => Named("openHandCursor");

    /// <summary>Resizing left and right.</summary>
    public static IntPtr ResizeLeftRight => Named("resizeLeftRightCursor");

    /// <summary>Resizing up and down.</summary>
    public static IntPtr ResizeUpDown => Named("resizeUpDownCursor");

    /// <summary>Resizing up.</summary>
    public static IntPtr ResizeUp => Named("resizeUpCursor");

    /// <summary>The crosshair.</summary>
    public static IntPtr Crosshair => Named("crosshairCursor");

    /// <summary>What cannot be done here.</summary>
    public static IntPtr OperationNotAllowed => Named("operationNotAllowedCursor");

    /// <summary>A drag that copies.</summary>
    public static IntPtr DragCopy => Named("dragCopyCursor");

    /// <summary>A drag that links.</summary>
    public static IntPtr DragLink => Named("dragLinkCursor");

    /// <summary>Makes <paramref name="cursor"/> the current cursor.</summary>
    public static void Set(IntPtr cursor) => NSObject.Send(cursor, SetSelector);

    /// <summary>Hides the cursor.</summary>
    public static void Hide() => NSObject.Send(Class, HideSelector);

    /// <summary>Shows the cursor again after a <see cref="Hide"/>.</summary>
    public static void Unhide() => NSObject.Send(Class, UnhideSelector);

    private static IntPtr Named(string selector) => NSObject.Send(Class, ObjCRuntime.RegisterSelector(selector));
}
