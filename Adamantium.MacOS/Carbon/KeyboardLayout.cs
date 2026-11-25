using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.Carbon;

/// <summary>The keyboard layouts the system types with (Text Input Sources). Called on the main thread only.</summary>
public static unsafe class KeyboardLayout
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const ushort KeyActionDown = 0;
    private const uint NoDeadKeys = 1;

    private static readonly IntPtr LayoutDataProperty = Marshal.ReadIntPtr(
        NativeLibrary.GetExport(NativeLibrary.Load(Carbon), "kTISPropertyUnicodeKeyLayoutData"));

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

    /// <summary>The character <paramref name="layout"/> has on the key of hardware code <paramref name="keyCode"/> with no
    /// modifier held; 0 for none.</summary>
    public static char CharacterOf(IntPtr layout, ushort keyCode)
    {
        var data = layout == IntPtr.Zero ? IntPtr.Zero : TISGetInputSourceProperty(layout, LayoutDataProperty);
        if (data == IntPtr.Zero)
            return '\0';

        uint deadKeys = 0;
        nuint length = 0;
        const int capacity = 4;
        var typed = stackalloc char[capacity];
        var status = UCKeyTranslate(CFDataGetBytePtr(data), keyCode, KeyActionDown, 0, LMGetKbdType(), NoDeadKeys,
            &deadKeys, capacity, &length, typed);
        return status == 0 && length == 1 ? typed[0] : '\0';
    }

    [DllImport(Carbon)]
    private static extern IntPtr TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

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
