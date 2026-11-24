using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.ObjectiveC;

/// <summary>Classes of the system frameworks, which the runtime knows only once their framework is loaded.</summary>
internal static class ObjCFrameworks
{
    private static readonly IntPtr Foundation =
        NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");

    private static readonly IntPtr AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");

    private static readonly IntPtr QuartzCore =
        NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");

    /// <summary>The class <paramref name="name"/> of Foundation, AppKit or QuartzCore.</summary>
    internal static IntPtr GetClass(string name)
    {
        GC.KeepAlive(Foundation);
        GC.KeepAlive(AppKit);
        GC.KeepAlive(QuartzCore);
        var found = ObjCRuntime.GetClass(name);
        return found != IntPtr.Zero ? found : throw new InvalidOperationException($"No Objective-C class {name}");
    }
}
