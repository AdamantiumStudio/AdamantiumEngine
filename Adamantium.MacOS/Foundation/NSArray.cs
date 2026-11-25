using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Makes and reads an NSArray.</summary>
public static unsafe class NSArray
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSArray");
    private static readonly IntPtr ArrayWithObjectsSelector = ObjCRuntime.RegisterSelector("arrayWithObjects:count:");
    private static readonly IntPtr CountSelector = ObjCRuntime.RegisterSelector("count");
    private static readonly IntPtr ObjectAtIndexSelector = ObjCRuntime.RegisterSelector("objectAtIndex:");

    /// <summary>An array of <paramref name="objects"/>, which it retains; autoreleased.</summary>
    public static IntPtr Create(ReadOnlySpan<IntPtr> objects)
    {
        fixed (IntPtr* first = objects)
        {
            return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, nuint, IntPtr>)ObjCRuntime.MessageSend)(Class,
                ArrayWithObjectsSelector, first, (nuint)objects.Length);
        }
    }

    /// <summary>How many objects the array has; none for a null one.</summary>
    public static int CountOf(IntPtr array) => array == IntPtr.Zero ? 0 : (int)NSObject.SendForInteger(array, CountSelector);

    /// <summary>The object at <paramref name="index"/>, owned by the array.</summary>
    public static IntPtr ObjectAt(IntPtr array, int index) =>
        ((delegate* unmanaged<IntPtr, IntPtr, nuint, IntPtr>)ObjCRuntime.MessageSend)(array, ObjectAtIndexSelector,
            (nuint)index);
}
