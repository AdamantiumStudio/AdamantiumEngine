using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Printing.Xps;

/// <summary>Changes a run of glyphs takes together: its indices, its text and how they map. Its slots as
/// xpsobjectmodel.h has them; those not used take raw pointers.</summary>
[ComImport]
[Guid("A5AB8616-5B16-4B9F-9629-89B323ED7909")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGlyphsEditor
{
    [PreserveSig] int ApplyEdits();
    [PreserveSig] int GetUnicodeString(out IntPtr text);
    [PreserveSig] int SetUnicodeString([MarshalAs(UnmanagedType.LPWStr)] string text);
    [PreserveSig] int GetGlyphIndexCount(out uint count);
    [PreserveSig] int GetGlyphIndices(ref uint count, IntPtr indices);

    [PreserveSig]
    int SetGlyphIndices(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] XpsGlyphIndex[] indices);
}
