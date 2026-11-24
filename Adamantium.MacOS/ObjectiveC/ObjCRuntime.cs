using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.ObjectiveC;

/// <summary>The Objective-C runtime (libobjc): classes, selectors, instance variables and autorelease pools. Messages
/// go through <see cref="MessageSend"/>, cast to the signature of the method called.</summary>
public static class ObjCRuntime
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    private static readonly IntPtr Handle = NativeLibrary.Load(Library);

    /// <summary>objc_msgSend: call it through a function pointer of the method's own signature.</summary>
    public static readonly IntPtr MessageSend = NativeLibrary.GetExport(Handle, "objc_msgSend");

    /// <summary>objc_msgSendSuper: as <see cref="MessageSend"/>, with an objc_super in place of the receiver.</summary>
    public static readonly IntPtr MessageSendSuper = NativeLibrary.GetExport(Handle, "objc_msgSendSuper");

    /// <summary>objc_getClass.</summary>
    [DllImport(Library, EntryPoint = "objc_getClass")]
    public static extern IntPtr GetClass(string name);

    /// <summary>object_getClass.</summary>
    [DllImport(Library, EntryPoint = "object_getClass")]
    public static extern IntPtr GetClassOf(IntPtr instance);

    /// <summary>class_getSuperclass.</summary>
    [DllImport(Library, EntryPoint = "class_getSuperclass")]
    public static extern IntPtr GetSuperclass(IntPtr objCClass);

    /// <summary>sel_registerName.</summary>
    [DllImport(Library, EntryPoint = "sel_registerName")]
    public static extern IntPtr RegisterSelector(string name);

    /// <summary>objc_getProtocol.</summary>
    [DllImport(Library, EntryPoint = "objc_getProtocol")]
    public static extern IntPtr GetProtocol(string name);

    /// <summary>objc_allocateClassPair.</summary>
    [DllImport(Library, EntryPoint = "objc_allocateClassPair")]
    public static extern IntPtr AllocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    /// <summary>objc_registerClassPair.</summary>
    [DllImport(Library, EntryPoint = "objc_registerClassPair")]
    public static extern void RegisterClassPair(IntPtr objCClass);

    /// <summary>class_addMethod.</summary>
    [DllImport(Library, EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool AddMethod(IntPtr objCClass, IntPtr selector, IntPtr implementation, string types);

    /// <summary>class_addIvar.</summary>
    [DllImport(Library, EntryPoint = "class_addIvar")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool AddInstanceVariable(IntPtr objCClass, string name, nuint size, byte alignment, string types);

    /// <summary>class_addProtocol.</summary>
    [DllImport(Library, EntryPoint = "class_addProtocol")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool AddProtocol(IntPtr objCClass, IntPtr protocol);

    /// <summary>class_getInstanceVariable.</summary>
    [DllImport(Library, EntryPoint = "class_getInstanceVariable")]
    public static extern IntPtr GetInstanceVariable(IntPtr objCClass, string name);

    /// <summary>ivar_getOffset.</summary>
    [DllImport(Library, EntryPoint = "ivar_getOffset")]
    public static extern nint GetInstanceVariableOffset(IntPtr instanceVariable);

    /// <summary>objc_autoreleasePoolPush.</summary>
    [DllImport(Library, EntryPoint = "objc_autoreleasePoolPush")]
    public static extern IntPtr PushAutoreleasePool();

    /// <summary>objc_autoreleasePoolPop.</summary>
    [DllImport(Library, EntryPoint = "objc_autoreleasePoolPop")]
    public static extern void PopAutoreleasePool(IntPtr pool);
}
