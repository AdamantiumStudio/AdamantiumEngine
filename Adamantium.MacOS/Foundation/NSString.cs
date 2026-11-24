using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Foundation strings to and from .NET strings.</summary>
public static unsafe class NSString
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSString");
    private static readonly IntPtr InitWithCharactersSelector = ObjCRuntime.RegisterSelector("initWithCharacters:length:");
    private static readonly IntPtr Utf8StringSelector = ObjCRuntime.RegisterSelector("UTF8String");

    /// <summary>A new NSString of <paramref name="text"/>, owned by the caller (<see cref="NSObject.Release"/>).</summary>
    public static IntPtr Create(string text)
    {
        text ??= string.Empty;
        fixed (char* characters = text)
        {
            return ((delegate* unmanaged<IntPtr, IntPtr, char*, nuint, IntPtr>)ObjCRuntime.MessageSend)(
                NSObject.Alloc(Class), InitWithCharactersSelector, characters, (nuint)text.Length);
        }
    }

    /// <summary>The text of an NSString; null for a null one.</summary>
    public static string ToManaged(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
            return null;

        return Marshal.PtrToStringUTF8(NSObject.Send(nsString, Utf8StringSelector));
    }
}
