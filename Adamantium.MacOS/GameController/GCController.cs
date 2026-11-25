using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.GameController;

/// <summary>Game controllers, and the parts of one of the shape every gamepad has (GCExtendedGamepad).</summary>
public static unsafe class GCController
{
    private static readonly IntPtr Framework =
        NativeLibrary.Load("/System/Library/Frameworks/GameController.framework/GameController");

    private static readonly IntPtr Class = ObjCFrameworks.GetClass("GCController");
    private static readonly IntPtr ControllersSelector = ObjCRuntime.RegisterSelector("controllers");
    private static readonly IntPtr ExtendedGamepadSelector = ObjCRuntime.RegisterSelector("extendedGamepad");
    private static readonly IntPtr VendorNameSelector = ObjCRuntime.RegisterSelector("vendorName");
    private static readonly IntPtr ProductCategorySelector = ObjCRuntime.RegisterSelector("productCategory");
    private static readonly IntPtr IsPressedSelector = ObjCRuntime.RegisterSelector("isPressed");
    private static readonly IntPtr ValueSelector = ObjCRuntime.RegisterSelector("value");
    private static readonly IntPtr XAxisSelector = ObjCRuntime.RegisterSelector("xAxis");
    private static readonly IntPtr YAxisSelector = ObjCRuntime.RegisterSelector("yAxis");
    private static readonly IntPtr HapticsSelector = ObjCRuntime.RegisterSelector("haptics");
    private static readonly IntPtr LocalitiesSelector = ObjCRuntime.RegisterSelector("supportedLocalities");
    private static readonly IntPtr ContainsObjectSelector = ObjCRuntime.RegisterSelector("containsObject:");
    private static readonly IntPtr CreateEngineSelector = ObjCRuntime.RegisterSelector("createEngineWithLocality:");

    /// <summary>The controllers connected, an NSArray.</summary>
    public static IntPtr Controllers
    {
        get
        {
            GC.KeepAlive(Framework);
            return NSObject.Send(Class, ControllersSelector);
        }
    }

    /// <summary>The controller's parts as a gamepad's; zero when it is none.</summary>
    public static IntPtr GamepadOf(IntPtr controller) => NSObject.Send(controller, ExtendedGamepadSelector);

    /// <summary>What the controller is called.</summary>
    public static string NameOf(IntPtr controller) => NSString.ToManaged(NSObject.Send(controller, VendorNameSelector));

    /// <summary>What kind it is: "DualSense", "Xbox One".</summary>
    public static string CategoryOf(IntPtr controller) =>
        NSString.ToManaged(NSObject.Send(controller, ProductCategorySelector));

    /// <summary>The place of the left handle's motor.</summary>
    public static IntPtr LeftHandle { get; } = Constant("GCHapticsLocalityLeftHandle");

    /// <summary>The place of the right handle's motor.</summary>
    public static IntPtr RightHandle { get; } = Constant("GCHapticsLocalityRightHandle");

    /// <summary>The place of the motor behind the left trigger.</summary>
    public static IntPtr LeftTrigger { get; } = Constant("GCHapticsLocalityLeftTrigger");

    /// <summary>The place of the motor behind the right trigger.</summary>
    public static IntPtr RightTrigger { get; } = Constant("GCHapticsLocalityRightTrigger");

    /// <summary>The place of whatever motors a gamepad has.</summary>
    public static IntPtr DefaultLocality { get; } = Constant("GCHapticsLocalityDefault");

    /// <summary>How long a pattern that goes on until stopped is.</summary>
    public static double InfiniteDuration { get; } = *(float*)NativeLibrary.GetExport(Framework, "GCHapticDurationInfinite");

    /// <summary>Whether the controller has a motor at a place.</summary>
    public static bool HasMotorAt(IntPtr controller, IntPtr locality)
    {
        var haptics = NSObject.Send(controller, HapticsSelector);
        return haptics != IntPtr.Zero &&
            ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(
                NSObject.Send(haptics, LocalitiesSelector), ContainsObjectSelector, locality) != 0;
    }

    /// <summary>A CHHapticEngine of the motor at a place, owned by the caller; zero when there is none.</summary>
    public static IntPtr CreateEngine(IntPtr controller, IntPtr locality)
    {
        var haptics = NSObject.Send(controller, HapticsSelector);
        return haptics == IntPtr.Zero
            ? IntPtr.Zero
            : NSObject.Retain(((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(haptics,
                CreateEngineSelector, locality));
    }

    /// <summary>A part of a gamepad, by the property's selector; zero when the gamepad has none.</summary>
    public static IntPtr PartOf(IntPtr gamepad, IntPtr selector) => NSObject.Send(gamepad, selector);

    /// <summary>Whether a button is down; not, for none.</summary>
    public static bool IsPressed(IntPtr button) => button != IntPtr.Zero && NSObject.SendForBoolean(button, IsPressedSelector);

    /// <summary>How far a button or an axis is moved; 0 for none.</summary>
    public static float ValueOf(IntPtr part) => part == IntPtr.Zero
        ? 0
        : ((delegate* unmanaged<IntPtr, IntPtr, float>)ObjCRuntime.MessageSend)(part, ValueSelector);

    /// <summary>The axis across of a stick.</summary>
    public static IntPtr XAxisOf(IntPtr stick) => NSObject.Send(stick, XAxisSelector);

    /// <summary>The axis up of a stick.</summary>
    public static IntPtr YAxisOf(IntPtr stick) => NSObject.Send(stick, YAxisSelector);

    private static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Framework, name));
}
