using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The messages every Objective-C object answers: making, keeping and letting go of it.</summary>
public static unsafe class NSObject
{
    private static readonly IntPtr AllocSelector = ObjCRuntime.RegisterSelector("alloc");
    private static readonly IntPtr InitSelector = ObjCRuntime.RegisterSelector("init");
    private static readonly IntPtr NewSelector = ObjCRuntime.RegisterSelector("new");
    private static readonly IntPtr RetainSelector = ObjCRuntime.RegisterSelector("retain");
    private static readonly IntPtr ReleaseSelector = ObjCRuntime.RegisterSelector("release");

    /// <summary>[class alloc]: an instance not initialized yet.</summary>
    public static IntPtr Alloc(IntPtr objCClass) => Send(objCClass, AllocSelector);

    /// <summary>[instance init].</summary>
    public static IntPtr Init(IntPtr instance) => Send(instance, InitSelector);

    /// <summary>[class new]: an instance made and initialized, owned by the caller.</summary>
    public static IntPtr New(IntPtr objCClass) => Send(objCClass, NewSelector);

    /// <summary>[instance retain].</summary>
    public static IntPtr Retain(IntPtr instance) => Send(instance, RetainSelector);

    /// <summary>[instance release]; nothing for a null instance.</summary>
    public static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            ((delegate* unmanaged<IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(instance, ReleaseSelector);
    }

    /// <summary>A message with no arguments that answers an object.</summary>
    public static IntPtr Send(IntPtr receiver, IntPtr selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(receiver, selector);

    /// <summary>A message with one object argument and no answer.</summary>
    public static void Send(IntPtr receiver, IntPtr selector, IntPtr argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(receiver, selector, argument);

    /// <summary>A message with one boolean argument and no answer.</summary>
    public static void Send(IntPtr receiver, IntPtr selector, bool argument) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte, void>)ObjCRuntime.MessageSend)(receiver, selector, argument ? (byte)1 : (byte)0);

    /// <summary>A message with no arguments that answers a boolean.</summary>
    public static bool SendForBoolean(IntPtr receiver, IntPtr selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte>)ObjCRuntime.MessageSend)(receiver, selector) != 0;

    /// <summary>A message with no arguments that answers a double (a CGFloat or an NSTimeInterval).</summary>
    public static double SendForDouble(IntPtr receiver, IntPtr selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double>)ObjCRuntime.MessageSend)(receiver, selector);

    /// <summary>A message with no arguments that answers an integer (an NSInteger or NSUInteger).</summary>
    public static nint SendForInteger(IntPtr receiver, IntPtr selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nint>)ObjCRuntime.MessageSend)(receiver, selector);
}
