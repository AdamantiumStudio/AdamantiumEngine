using System;
using System.Runtime.InteropServices;

namespace Adamantium.MacOS.CoreGraphics;

/// <summary>The Core Graphics and Core Foundation calls that make an image from pixels and tell displays apart.</summary>
public static unsafe class CoreGraphicsInterop
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>CFDataCreate: a copy of the bytes.</summary>
    [DllImport(CoreFoundation, EntryPoint = "CFDataCreate")]
    public static extern IntPtr CreateData(IntPtr allocator, byte* bytes, nint length);

    /// <summary>CFRelease.</summary>
    [DllImport(CoreFoundation, EntryPoint = "CFRelease")]
    public static extern void Release(IntPtr value);

    /// <summary>CGColorSpaceCreateDeviceRGB.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGColorSpaceCreateDeviceRGB")]
    public static extern IntPtr CreateDeviceRgbColorSpace();

    /// <summary>CGDataProviderCreateWithCFData.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGDataProviderCreateWithCFData")]
    public static extern IntPtr CreateDataProvider(IntPtr data);

    /// <summary>CGImageCreate.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGImageCreate")]
    public static extern IntPtr CreateImage(nuint width, nuint height, nuint bitsPerComponent, nuint bitsPerPixel,
        nuint bytesPerRow, IntPtr colorSpace, uint bitmapInfo, IntPtr provider, double* decode,
        [MarshalAs(UnmanagedType.U1)] bool shouldInterpolate, int intent);

    /// <summary>CGDisplayVendorNumber.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGDisplayVendorNumber")]
    public static extern uint DisplayVendor(uint display);

    /// <summary>CGDisplayModelNumber.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGDisplayModelNumber")]
    public static extern uint DisplayModel(uint display);

    /// <summary>CGDisplaySerialNumber.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGDisplaySerialNumber")]
    public static extern uint DisplaySerial(uint display);

    /// <summary>CGEventSourceKeyState: whether a key, by its hardware code, is down; <paramref name="state"/> 1 is the
    /// hardware's.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGEventSourceKeyState")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool IsKeyDown(int state, ushort keyCode);

    /// <summary>CGEventSourceFlagsState: the modifiers held and locked.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGEventSourceFlagsState")]
    public static extern ulong ModifierFlags(int state);

    /// <summary>CGWarpMouseCursorPosition: the cursor put at a point from the top left corner of the main display, with no
    /// event of it.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGWarpMouseCursorPosition")]
    public static extern int WarpCursor(CGPoint position);

    /// <summary>CGAssociateMouseAndMouseCursorPosition: whether the cursor goes with the mouse; off, the mouse still says
    /// how far it moves.</summary>
    [DllImport(CoreGraphics, EntryPoint = "CGAssociateMouseAndMouseCursorPosition")]
    public static extern int AssociateMouseAndCursor([MarshalAs(UnmanagedType.Bool)] bool connected);
}
