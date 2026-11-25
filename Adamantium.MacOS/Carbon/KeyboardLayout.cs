using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;

namespace Adamantium.MacOS.Carbon;

/// <summary>The keyboard layouts the system types with (Text Input Sources). Called on the main thread only.</summary>
public static unsafe class KeyboardLayout
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const ushort KeyActionDown = 0;
    private const uint NoDeadKeys = 1;
    private const uint CommandKey = 1;

    private static readonly IntPtr LayoutDataProperty = Marshal.ReadIntPtr(
        NativeLibrary.GetExport(NativeLibrary.Load(Carbon), "kTISPropertyUnicodeKeyLayoutData"));

    private static readonly IntPtr LanguagesProperty = Marshal.ReadIntPtr(
        NativeLibrary.GetExport(NativeLibrary.Load(Carbon), "kTISPropertyInputSourceLanguages"));

    /// <summary>The notification, told to every application, of the user adding or taking away a layout.</summary>
    public const string EnabledChangedNotification = "com.apple.Carbon.TISNotifyEnabledKeyboardInputSourcesChanged";

    /// <summary>The layout in use, or, where it types no Latin letters, the one in use that does; the caller releases it.
    /// </summary>
    public static IntPtr CopyCurrentLatin() => TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

    /// <summary>Whether two layouts are the same.</summary>
    public static bool AreEqual(IntPtr first, IntPtr second) =>
        first == second || first != IntPtr.Zero && second != IntPtr.Zero && CFEqual(first, second) != 0;

    /// <summary>Lets go of a layout; nothing for none.</summary>
    public static void Release(IntPtr layout)
    {
        if (layout != IntPtr.Zero)
            CFRelease(layout);
    }

    /// <summary>The languages of the layouts and input methods the user switches between, each one's own first.</summary>
    public static string[] EnabledLanguages()
    {
        var sources = TISCreateInputSourceList(IntPtr.Zero, 0);
        var languages = new System.Collections.Generic.List<string>();
        for (var i = 0; i < NSArray.CountOf(sources); i++)
        {
            var listed = TISGetInputSourceProperty(NSArray.ObjectAt(sources, i), LanguagesProperty);
            if (NSArray.CountOf(listed) > 0)
                languages.Add(NSString.ToManaged(NSArray.ObjectAt(listed, 0)));
        }

        Release(sources);
        return languages.ToArray();
    }

    /// <summary>The character <paramref name="layout"/> has on the key of hardware code <paramref name="keyCode"/>, with
    /// Command held or with nothing - a layout may have others for shortcuts; 0 for none.</summary>
    public static char CharacterOf(IntPtr layout, ushort keyCode, bool withCommand = false)
    {
        var data = layout == IntPtr.Zero ? IntPtr.Zero : TISGetInputSourceProperty(layout, LayoutDataProperty);
        if (data == IntPtr.Zero)
            return '\0';

        uint deadKeys = 0;
        nuint length = 0;
        const int capacity = 4;
        var typed = stackalloc char[capacity];
        var status = UCKeyTranslate(CFDataGetBytePtr(data), keyCode, KeyActionDown,
            withCommand ? CommandKey : 0, LMGetKbdType(), NoDeadKeys,
            &deadKeys, capacity, &length, typed);
        return status == 0 && length == 1 ? typed[0] : '\0';
    }

    [DllImport(Carbon)]
    private static extern IntPtr TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

    [DllImport(Carbon)]
    private static extern IntPtr TISCreateInputSourceList(IntPtr properties, byte includeAllInstalled);

    [DllImport(Carbon)]
    private static extern IntPtr TISGetInputSourceProperty(IntPtr source, IntPtr property);

    [DllImport(Carbon)]
    private static extern byte LMGetKbdType();

    [DllImport(Carbon)]
    private static extern int UCKeyTranslate(IntPtr layout, ushort keyCode, ushort action, uint modifiers,
        uint keyboardType, uint options, uint* deadKeyState, nuint capacity, nuint* length, char* text);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern byte CFEqual(IntPtr first, IntPtr second);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);
}
