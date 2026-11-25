using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>The user's settings, the system's among them.</summary>
public static unsafe class NSUserDefaults
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSUserDefaults");
    private static readonly IntPtr StandardSelector = ObjCRuntime.RegisterSelector("standardUserDefaults");
    private static readonly IntPtr StringForKeySelector = ObjCRuntime.RegisterSelector("stringForKey:");

    /// <summary>The text set for <paramref name="key"/>; null when none is.</summary>
    public static string StringOf(string key)
    {
        var nsKey = NSString.Create(key);
        var value = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Send(Class, StandardSelector), StringForKeySelector, nsKey);
        NSObject.Release(nsKey);
        return NSString.ToManaged(value);
    }
}
