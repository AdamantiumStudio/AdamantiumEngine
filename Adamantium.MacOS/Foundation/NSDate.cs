using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The dates an event loop waits until.</summary>
public static class NSDate
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSDate");
    private static readonly IntPtr DistantFutureSelector = ObjCRuntime.RegisterSelector("distantFuture");
    private static readonly IntPtr DistantPastSelector = ObjCRuntime.RegisterSelector("distantPast");

    /// <summary>A date that never comes: wait until an event arrives.</summary>
    public static IntPtr DistantFuture => NSObject.Send(Class, DistantFutureSelector);

    /// <summary>A date already gone: take an event only if one is waiting.</summary>
    public static IntPtr DistantPast => NSObject.Send(Class, DistantPastSelector);
}
