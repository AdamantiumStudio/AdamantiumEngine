using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.ObjectiveC;

/// <summary>Makes an Objective-C class at run time - a subclass whose methods are C# functions - with an instance
/// variable that holds the managed object each instance stands for (see <see cref="ObjCManagedObject"/>).</summary>
public sealed class ObjCClassBuilder
{
    private readonly IntPtr _class;

    /// <summary>Starts a class <paramref name="name"/> under <paramref name="superclass"/>.</summary>
    public ObjCClassBuilder(string name, string superclass)
    {
        _class = ObjCRuntime.AllocateClassPair(ObjCRuntime.GetClass(superclass), name, 0);
        if (_class == IntPtr.Zero)
            throw new InvalidOperationException($"The Objective-C class {name} exists already");

        ObjCRuntime.AddInstanceVariable(_class, ObjCManagedObject.InstanceVariable, (nuint)IntPtr.Size,
            (byte)Math.Log2(IntPtr.Size), "^v");
    }

    /// <summary>Adds a method: <paramref name="implementation"/> is an unmanaged function taking the receiver and the
    /// selector first, <paramref name="types"/> its Objective-C type encoding.</summary>
    public ObjCClassBuilder AddMethod(string selector, IntPtr implementation, string types)
    {
        if (!ObjCRuntime.AddMethod(_class, ObjCRuntime.RegisterSelector(selector), implementation, types))
            throw new InvalidOperationException($"The method {selector} was not added");

        return this;
    }

    /// <summary>Declares that the class conforms to <paramref name="protocol"/>.</summary>
    public ObjCClassBuilder AddProtocol(string protocol)
    {
        var found = ObjCRuntime.GetProtocol(protocol);
        if (found != IntPtr.Zero)
            ObjCRuntime.AddProtocol(_class, found);

        return this;
    }

    /// <summary>Registers the class; it can be instantiated from then on.</summary>
    public IntPtr Register()
    {
        ObjCRuntime.RegisterClassPair(_class);
        return _class;
    }
}
