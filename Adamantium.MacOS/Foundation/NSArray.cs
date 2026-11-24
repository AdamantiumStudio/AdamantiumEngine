using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Reads an NSArray.</summary>
public static unsafe class NSArray
{
    private static readonly IntPtr CountSelector = ObjCRuntime.RegisterSelector("count");
    private static readonly IntPtr ObjectAtIndexSelector = ObjCRuntime.RegisterSelector("objectAtIndex:");

    /// <summary>How many objects the array has; none for a null one.</summary>
    public static int CountOf(IntPtr array) => array == IntPtr.Zero ? 0 : (int)NSObject.SendForInteger(array, CountSelector);

    /// <summary>The object at <paramref name="index"/>, owned by the array.</summary>
    public static IntPtr ObjectAt(IntPtr array, int index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, IntPtr>)ObjCRuntime.MessageSend)(array, ObjectAtIndexSelector,
            (nuint)index);
}
