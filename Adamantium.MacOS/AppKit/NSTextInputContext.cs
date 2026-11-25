using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>The input method of a view that takes text (NSTextInputClient). Main thread only.</summary>
public static unsafe class NSTextInputContext
{
    private static readonly IntPtr AppKit = NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    private static readonly IntPtr RomanSources = Marshal.ReadIntPtr(
        NativeLibrary.GetExport(AppKit, "NSAllRomanInputSourcesLocaleIdentifier"));
    private static readonly IntPtr InputContextSelector = ObjCRuntime.RegisterSelector("inputContext");
    private static readonly IntPtr HandleEventSelector = ObjCRuntime.RegisterSelector("handleEvent:");
    private static readonly IntPtr DiscardSelector = ObjCRuntime.RegisterSelector("discardMarkedText");
    private static readonly IntPtr InvalidateSelector = ObjCRuntime.RegisterSelector("invalidateCharacterCoordinates");
    private static readonly IntPtr SetLocalesSelector = ObjCRuntime.RegisterSelector("setAllowedInputSourceLocales:");
    private static readonly IntPtr ConvertToWindowSelector = ObjCRuntime.RegisterSelector("convertRect:toView:");
    private static readonly IntPtr ConvertToScreenSelector = ObjCRuntime.RegisterSelector("convertRectToScreen:");
    private static readonly IntPtr PaletteSelector = ObjCRuntime.RegisterSelector("orderFrontCharacterPalette:");

    /// <summary>The input method of a view; zero when the view takes no text.</summary>
    public static IntPtr Of(IntPtr view) => NSObject.Send(view, InputContextSelector);

    /// <summary>Hands a key to the input method, which tells the view what comes of it; false when it has no use for it.
    /// </summary>
    public static bool HandleEvent(IntPtr context, IntPtr nsEvent) =>
        NSObject.SendForBoolean(context, HandleEventSelector, nsEvent);

    /// <summary>Has the input method forget what it composes.</summary>
    public static void DiscardMarkedText(IntPtr context) =>
        ((delegate* unmanaged<IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(context, DiscardSelector);

    /// <summary>Has the input method ask anew where the caret is.</summary>
    public static void InvalidateCoordinates(IntPtr context) =>
        ((delegate* unmanaged<IntPtr, IntPtr, void>)ObjCRuntime.MessageSend)(context, InvalidateSelector);

    /// <summary>Lets only the keyboards of Latin letters type, as into a password; or any.</summary>
    public static void AllowOnlyRoman(IntPtr context, bool onlyRoman) =>
        NSObject.Send(context, SetLocalesSelector, onlyRoman ? NSArray.Create([RomanSources]) : IntPtr.Zero);

    /// <summary>Where a rectangle of a view is on the screen.</summary>
    public static CGRect ToScreen(IntPtr view, CGRect rectangle)
    {
        var inWindow = ((delegate* unmanaged<IntPtr, IntPtr, CGRect, IntPtr, CGRect>)ObjCRuntime.MessageSend)(view,
            ConvertToWindowSelector, rectangle, IntPtr.Zero);
        return ((delegate* unmanaged<IntPtr, IntPtr, CGRect, CGRect>)ObjCRuntime.MessageSend)(NSView.WindowOf(view),
            ConvertToScreenSelector, inWindow);
    }

    /// <summary>Opens the system's panel of emoji and symbols.</summary>
    public static void ShowCharacters() => NSObject.Send(
        NSObject.Send(ObjCFrameworks.GetClass("NSApplication"), ObjCRuntime.RegisterSelector("sharedApplication")),
        PaletteSelector, IntPtr.Zero);
}
