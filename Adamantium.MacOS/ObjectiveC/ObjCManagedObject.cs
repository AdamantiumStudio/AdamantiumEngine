using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.ObjectiveC;

/// <summary>The managed object an instance of a class made by <see cref="ObjCClassBuilder"/> stands for, kept in that
/// instance's own instance variable - no table of instances.</summary>
public static unsafe class ObjCManagedObject
{
    /// <summary>The name of the instance variable that holds the object.</summary>
    public const string InstanceVariable = "adamantiumManaged";

    /// <summary>Gives <paramref name="instance"/> its managed object; <see cref="Release"/> lets it go.</summary>
    public static void Attach(IntPtr instance, object target)
    {
        *Slot(instance) = GCHandle.ToIntPtr(GCHandle.Alloc(target));
    }

    /// <summary>The managed object of <paramref name="instance"/>; null when it has none.</summary>
    public static T Get<T>(IntPtr instance) where T : class
    {
        var handle = *Slot(instance);
        return handle == IntPtr.Zero ? null : GCHandle.FromIntPtr(handle).Target as T;
    }

    /// <summary>Lets the managed object of <paramref name="instance"/> go.</summary>
    public static void Release(IntPtr instance)
    {
        var slot = Slot(instance);
        if (*slot == IntPtr.Zero)
            return;

        GCHandle.FromIntPtr(*slot).Free();
        *slot = IntPtr.Zero;
    }

    private static IntPtr* Slot(IntPtr instance)
    {
        var variable = ObjCRuntime.GetInstanceVariable(ObjCRuntime.GetClassOf(instance), InstanceVariable);
        return (IntPtr*)((byte*)instance + ObjCRuntime.GetInstanceVariableOffset(variable));
    }
}
