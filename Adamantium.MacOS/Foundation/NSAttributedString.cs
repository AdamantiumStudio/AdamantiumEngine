using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Text with attributes, which are left out.</summary>
public static unsafe class NSAttributedString
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSAttributedString");
    private static readonly IntPtr InitSelector = ObjCRuntime.RegisterSelector("initWithString:");
    private static readonly IntPtr StringSelector = ObjCRuntime.RegisterSelector("string");
    private static readonly IntPtr IsKindOfClassSelector = ObjCRuntime.RegisterSelector("isKindOfClass:");
    private static readonly IntPtr AutoreleaseSelector = ObjCRuntime.RegisterSelector("autorelease");

    /// <summary>The text with no attributes, autoreleased.</summary>
    public static IntPtr Create(string text)
    {
        var nsText = NSString.Create(text);
        var attributed = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)ObjCRuntime.MessageSend)(
            NSObject.Alloc(Class), InitSelector, nsText);
        NSObject.Release(nsText);
        return NSObject.Send(attributed, AutoreleaseSelector);
    }

    /// <summary>The text of an NSString or an NSAttributedString; null for none.</summary>
    public static string TextOf(IntPtr text) => NSString.ToManaged(
        text != IntPtr.Zero && NSObject.SendForBoolean(text, IsKindOfClassSelector, Class)
            ? NSObject.Send(text, StringSelector)
            : text);
}
