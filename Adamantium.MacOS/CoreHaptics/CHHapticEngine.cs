using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.CoreHaptics;

/// <summary>A motor the system runs patterns on, here one that goes on until stopped, its strength changed as it plays.
/// </summary>
public static unsafe class CHHapticEngine
{
    private static readonly IntPtr Library =
        NativeLibrary.Load("/System/Library/Frameworks/CoreHaptics.framework/CoreHaptics");

    private static readonly IntPtr EventClass = GetClass("CHHapticEvent");
    private static readonly IntPtr EventParameterClass = GetClass("CHHapticEventParameter");
    private static readonly IntPtr DynamicParameterClass = GetClass("CHHapticDynamicParameter");
    private static readonly IntPtr PatternClass = GetClass("CHHapticPattern");
    private static readonly IntPtr Continuous = Constant("CHHapticEventTypeHapticContinuous");
    private static readonly IntPtr Intensity = Constant("CHHapticEventParameterIDHapticIntensity");
    private static readonly IntPtr Sharpness = Constant("CHHapticEventParameterIDHapticSharpness");
    private static readonly IntPtr IntensityControl = Constant("CHHapticDynamicParameterIDHapticIntensityControl");

    private static readonly IntPtr InitParameterSelector = ObjCRuntime.RegisterSelector("initWithParameterID:value:");
    private static readonly IntPtr InitDynamicSelector =
        ObjCRuntime.RegisterSelector("initWithParameterID:value:relativeTime:");
    private static readonly IntPtr InitEventSelector =
        ObjCRuntime.RegisterSelector("initWithEventType:parameters:relativeTime:duration:");
    private static readonly IntPtr InitPatternSelector = ObjCRuntime.RegisterSelector("initWithEvents:parameters:error:");
    private static readonly IntPtr SetHapticsOnlySelector = ObjCRuntime.RegisterSelector("setPlaysHapticsOnly:");
    private static readonly IntPtr StartSelector = ObjCRuntime.RegisterSelector("startAndReturnError:");
    private static readonly IntPtr StopSelector = ObjCRuntime.RegisterSelector("stopWithCompletionHandler:");
    private static readonly IntPtr CreatePlayerSelector = ObjCRuntime.RegisterSelector("createPlayerWithPattern:error:");
    private static readonly IntPtr StartAtSelector = ObjCRuntime.RegisterSelector("startAtTime:error:");
    private static readonly IntPtr SendParametersSelector = ObjCRuntime.RegisterSelector("sendParameters:atTime:error:");

    /// <summary>A pattern at full strength without end, owned by the caller; <paramref name="sharpness"/> from 0, a deep
    /// rumble, to 1, a fine buzz. Zero when the system makes none.</summary>
    public static IntPtr CreateEndlessPattern(float sharpness, double duration)
    {
        var intensityParameter = CreateParameter(Intensity, 1);
        var sharpnessParameter = CreateParameter(Sharpness, sharpness);
        var hapticEvent = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, double, double, IntPtr>)
            ObjCRuntime.MessageSend)(NSObject.Alloc(EventClass), InitEventSelector, Continuous,
            NSArray.Create([intensityParameter, sharpnessParameter]), 0, duration);
        var pattern = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(PatternClass), InitPatternSelector, NSArray.Create([hapticEvent]), NSArray.Create([]), null);
        NSObject.Release(intensityParameter);
        NSObject.Release(sharpnessParameter);
        NSObject.Release(hapticEvent);
        return pattern;
    }

    /// <summary>Starts the engine and waits until it runs; false when it does not.</summary>
    public static bool Start(IntPtr engine)
    {
        NSObject.Send(engine, SetHapticsOnlySelector, true);
        return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, byte>)ObjCRuntime.MessageSend)(engine, StartSelector, null) != 0;
    }

    /// <summary>Stops the engine, and what plays on it.</summary>
    public static void Stop(IntPtr engine) => NSObject.Send(engine, StopSelector, IntPtr.Zero);

    /// <summary>A player of <paramref name="pattern"/> on the engine, autoreleased; zero when there is none.</summary>
    public static IntPtr CreatePlayer(IntPtr engine, IntPtr pattern) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr*, IntPtr>)ObjCRuntime.MessageSend)(engine,
            CreatePlayerSelector, pattern, null);

    /// <summary>Plays the pattern from now; false when the engine does not run.</summary>
    public static bool Play(IntPtr player) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, IntPtr*, byte>)ObjCRuntime.MessageSend)(player, StartAtSelector, 0,
            null) != 0;

    /// <summary>Changes how strong the pattern plays, 0 to 1; false when the engine does not run.</summary>
    public static bool SetIntensity(IntPtr player, float intensity)
    {
        var parameter = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, float, double, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(DynamicParameterClass), InitDynamicSelector, IntensityControl, intensity, 0);
        var sent = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, double, IntPtr*, byte>)ObjCRuntime.MessageSend)(player,
            SendParametersSelector, NSArray.Create([parameter]), 0, null) != 0;
        NSObject.Release(parameter);
        return sent;
    }

    private static IntPtr CreateParameter(IntPtr id, float value) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, float, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(EventParameterClass), InitParameterSelector, id, value);

    private static IntPtr GetClass(string name)
    {
        GC.KeepAlive(Library);
        return ObjCFrameworks.GetClass(name);
    }

    private static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Library, name));
}
