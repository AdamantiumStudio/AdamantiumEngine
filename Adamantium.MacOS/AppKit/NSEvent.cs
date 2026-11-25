using System;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>What an event says: its kind, where, which key or button, the modifiers held; and an application-defined
/// event to wake the event loop with.</summary>
public static unsafe class NSEvent
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSEvent");
    private static readonly IntPtr TypeSelector = ObjCRuntime.RegisterSelector("type");
    private static readonly IntPtr SubtypeSelector = ObjCRuntime.RegisterSelector("subtype");
    private static readonly IntPtr WindowSelector = ObjCRuntime.RegisterSelector("window");
    private static readonly IntPtr LocationInWindowSelector = ObjCRuntime.RegisterSelector("locationInWindow");
    private static readonly IntPtr ModifierFlagsSelector = ObjCRuntime.RegisterSelector("modifierFlags");
    private static readonly IntPtr KeyCodeSelector = ObjCRuntime.RegisterSelector("keyCode");
    private static readonly IntPtr IsARepeatSelector = ObjCRuntime.RegisterSelector("isARepeat");
    private static readonly IntPtr CharactersSelector = ObjCRuntime.RegisterSelector("characters");
    private static readonly IntPtr ButtonNumberSelector = ObjCRuntime.RegisterSelector("buttonNumber");
    private static readonly IntPtr TimestampSelector = ObjCRuntime.RegisterSelector("timestamp");
    private static readonly IntPtr ScrollingDeltaXSelector = ObjCRuntime.RegisterSelector("scrollingDeltaX");
    private static readonly IntPtr ScrollingDeltaYSelector = ObjCRuntime.RegisterSelector("scrollingDeltaY");
    private static readonly IntPtr HasPreciseScrollingDeltasSelector =
        ObjCRuntime.RegisterSelector("hasPreciseScrollingDeltas");
    private static readonly IntPtr IsDirectionInvertedFromDeviceSelector =
        ObjCRuntime.RegisterSelector("isDirectionInvertedFromDevice");
    private static readonly IntPtr PhaseSelector = ObjCRuntime.RegisterSelector("phase");
    private static readonly IntPtr MomentumPhaseSelector = ObjCRuntime.RegisterSelector("momentumPhase");
    private static readonly IntPtr MagnificationSelector = ObjCRuntime.RegisterSelector("magnification");
    private static readonly IntPtr RotationSelector = ObjCRuntime.RegisterSelector("rotation");
    private static readonly IntPtr DoubleClickIntervalSelector = ObjCRuntime.RegisterSelector("doubleClickInterval");
    private static readonly IntPtr KeyRepeatDelaySelector = ObjCRuntime.RegisterSelector("keyRepeatDelay");
    private static readonly IntPtr KeyRepeatIntervalSelector = ObjCRuntime.RegisterSelector("keyRepeatInterval");
    private static readonly IntPtr MouseLocationSelector = ObjCRuntime.RegisterSelector("mouseLocation");
    private static readonly IntPtr DeltaXSelector = ObjCRuntime.RegisterSelector("deltaX");
    private static readonly IntPtr DeltaYSelector = ObjCRuntime.RegisterSelector("deltaY");
    private static readonly IntPtr OtherEventSelector = ObjCRuntime.RegisterSelector(
        "otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:");

    /// <summary>The event's kind.</summary>
    public static NSEventType TypeOf(IntPtr nsEvent) => (NSEventType)NSObject.SendForInteger(nsEvent, TypeSelector);

    /// <summary>The subtype of an application-defined event.</summary>
    public static short SubtypeOf(IntPtr nsEvent) => (short)NSObject.SendForInteger(nsEvent, SubtypeSelector);

    /// <summary>The NSWindow the event is for; zero for none.</summary>
    public static IntPtr WindowOf(IntPtr nsEvent) => NSObject.Send(nsEvent, WindowSelector);

    /// <summary>Where the pointer was, in the window's points from its bottom-left corner.</summary>
    public static CGPoint LocationInWindowOf(IntPtr nsEvent) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGPoint>)ObjCRuntime.MessageSend)(nsEvent, LocationInWindowSelector);

    /// <summary>The modifier keys held.</summary>
    public static NSEventModifierFlags ModifierFlagsOf(IntPtr nsEvent) =>
        (NSEventModifierFlags)NSObject.SendForInteger(nsEvent, ModifierFlagsSelector);

    /// <summary>The hardware key code of a key event (kVK_*).</summary>
    public static ushort KeyCodeOf(IntPtr nsEvent) =>
        ((delegate* unmanaged<IntPtr, IntPtr, ushort>)ObjCRuntime.MessageSend)(nsEvent, KeyCodeSelector);

    /// <summary>Whether a key-down event repeats a key held.</summary>
    public static bool IsRepeat(IntPtr nsEvent) => NSObject.SendForBoolean(nsEvent, IsARepeatSelector);

    /// <summary>The text a key event types.</summary>
    public static string CharactersOf(IntPtr nsEvent) => NSString.ToManaged(NSObject.Send(nsEvent, CharactersSelector));

    /// <summary>The mouse button of a button event: 0 left, 1 right, 2 middle, then the others.</summary>
    public static nint ButtonNumberOf(IntPtr nsEvent) => NSObject.SendForInteger(nsEvent, ButtonNumberSelector);

    /// <summary>When the event happened, in seconds since the system started.</summary>
    public static double TimestampOf(IntPtr nsEvent) => NSObject.SendForDouble(nsEvent, TimestampSelector);

    /// <summary>How far a scroll event scrolls across.</summary>
    public static double ScrollingDeltaXOf(IntPtr nsEvent) => NSObject.SendForDouble(nsEvent, ScrollingDeltaXSelector);

    /// <summary>How far a scroll event scrolls down the page (positive: towards the top).</summary>
    public static double ScrollingDeltaYOf(IntPtr nsEvent) => NSObject.SendForDouble(nsEvent, ScrollingDeltaYSelector);

    /// <summary>Whether a scroll event comes in points from a trackpad rather than in lines from a wheel.</summary>
    public static bool HasPreciseScrollingDeltas(IntPtr nsEvent) =>
        NSObject.SendForBoolean(nsEvent, HasPreciseScrollingDeltasSelector);

    /// <summary>Whether the deltas go with the fingers (natural scrolling) rather than against them.</summary>
    public static bool IsDirectionInvertedFromDevice(IntPtr nsEvent) =>
        NSObject.SendForBoolean(nsEvent, IsDirectionInvertedFromDeviceSelector);

    /// <summary>Where a gesture is; none for a wheel and for what the system goes on scrolling by itself.</summary>
    public static NSEventPhase PhaseOf(IntPtr nsEvent) => (NSEventPhase)NSObject.SendForInteger(nsEvent, PhaseSelector);

    /// <summary>Where the scrolling that goes on after the fingers lift is; none for a gesture of fingers.</summary>
    public static NSEventPhase MomentumPhaseOf(IntPtr nsEvent) =>
        (NSEventPhase)NSObject.SendForInteger(nsEvent, MomentumPhaseSelector);

    /// <summary>How much the fingers of a magnify event spread since the last one: 0.1 for a tenth farther apart.</summary>
    public static double MagnificationOf(IntPtr nsEvent) => NSObject.SendForDouble(nsEvent, MagnificationSelector);

    /// <summary>How far the fingers of a rotate event turned since the last one, in degrees counterclockwise.</summary>
    public static float RotationOf(IntPtr nsEvent) =>
        ((delegate* unmanaged<IntPtr, IntPtr, float>)ObjCRuntime.MessageSend)(nsEvent, RotationSelector);

    /// <summary>The longest time between the clicks of a double click, in seconds.</summary>
    public static double DoubleClickInterval => NSObject.SendForDouble(Class, DoubleClickIntervalSelector);

    /// <summary>How long a key held waits before it repeats, in seconds.</summary>
    public static double KeyRepeatDelay => NSObject.SendForDouble(Class, KeyRepeatDelaySelector);

    /// <summary>The time between two repeats of a key held, in seconds.</summary>
    public static double KeyRepeatInterval => NSObject.SendForDouble(Class, KeyRepeatIntervalSelector);

    /// <summary>Where the cursor is now, in AppKit's points of the desktop.</summary>
    public static CGPoint MouseLocation =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGPoint>)ObjCRuntime.MessageSend)(Class, MouseLocationSelector);

    /// <summary>How far the mouse itself moved across, in points.</summary>
    public static double DeltaXOf(IntPtr nsEvent) => NSObject.SendForDouble(nsEvent, DeltaXSelector);

    /// <summary>How far the mouse itself moved down, in points.</summary>
    public static double DeltaYOf(IntPtr nsEvent) => NSObject.SendForDouble(nsEvent, DeltaYSelector);

    /// <summary>An application-defined event of <paramref name="subtype"/>, autoreleased.</summary>
    public static IntPtr CreateApplicationDefined(short subtype) =>
        ((delegate* unmanaged<IntPtr, IntPtr, ulong, CGPoint, ulong, double, nint, IntPtr, short, nint, nint, IntPtr>)
            ObjCRuntime.MessageSend)(Class, OtherEventSelector, (ulong)NSEventType.ApplicationDefined, default, 0, 0, 0,
            IntPtr.Zero, subtype, 0, 0);
}
