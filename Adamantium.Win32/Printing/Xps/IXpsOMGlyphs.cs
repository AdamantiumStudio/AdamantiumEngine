using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.Win32.Printing.Xps;

// Text on an XPS page: glyphs of a font embedded in the package. Each interface lists its bases' slots first, as its
// vtable has them (xpsobjectmodel.h); slots not used take raw pointers.

/// <summary>A glyph's place in a run of glyphs, in hundredths of the em (XPS 1.0, 12.1.3): which glyph, how far the pen
/// moves after it, and how far it is moved from the pen - up for the vertical.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct XpsGlyphIndex(int index, float advanceWidth, float horizontalOffset, float verticalOffset)
{
    public int Index = index;
    public float AdvanceWidth = advanceWidth;
    public float HorizontalOffset = horizontalOffset;
    public float VerticalOffset = verticalOffset;
}

/// <summary>How a font is embedded (XPS_FONT_EMBEDDING): obfuscated as a font's license allows it to be printed.</summary>
public enum XpsFontEmbedding
{
    Normal = 1,
    Obfuscated = 2,
    Restricted = 3,
    RestrictedUnobfuscated = 4,
}

/// <summary>A run of glyphs of one font at one size, from an origin, filled by a brush.</summary>
[ComImport]
[Guid("819B3199-0A5A-4B64-BEC7-A9E17E780DE2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGlyphs
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetTransform(out IntPtr transform);
    [PreserveSig] int GetTransformLocal(out IntPtr transform);
    [PreserveSig] int SetTransformLocal(IXpsOMMatrixTransform transform);
    [PreserveSig] int GetTransformLookup(out IntPtr key);
    [PreserveSig] int SetTransformLookup(IntPtr key);
    [PreserveSig] int GetClipGeometry(out IntPtr geometry);
    [PreserveSig] int GetClipGeometryLocal(out IntPtr geometry);
    [PreserveSig] int SetClipGeometryLocal(IXpsOMGeometry geometry);
    [PreserveSig] int GetClipGeometryLookup(out IntPtr key);
    [PreserveSig] int SetClipGeometryLookup(IntPtr key);
    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);
    [PreserveSig] int GetOpacityMaskBrush(out IntPtr brush);
    [PreserveSig] int GetOpacityMaskBrushLocal(out IntPtr brush);
    [PreserveSig] int SetOpacityMaskBrushLocal(IXpsOMBrush brush);
    [PreserveSig] int GetOpacityMaskBrushLookup(out IntPtr key);
    [PreserveSig] int SetOpacityMaskBrushLookup(IntPtr key);
    [PreserveSig] int GetName(out IntPtr name);
    [PreserveSig] int SetName(IntPtr name);
    [PreserveSig] int GetIsHyperlinkTarget(out int isTarget);
    [PreserveSig] int SetIsHyperlinkTarget(int isTarget);
    [PreserveSig] int GetHyperlinkNavigateUri(out IntPtr uri);
    [PreserveSig] int SetHyperlinkNavigateUri(IntPtr uri);
    [PreserveSig] int GetLanguage(out IntPtr language);
    [PreserveSig] int SetLanguage(IntPtr language);

    [PreserveSig] int GetUnicodeString(out IntPtr text);
    [PreserveSig] int GetGlyphIndexCount(out uint count);
    [PreserveSig] int GetGlyphIndices(ref uint count, IntPtr indices);
    [PreserveSig] int GetGlyphMappingCount(out uint count);
    [PreserveSig] int GetGlyphMappings(ref uint count, IntPtr mappings);
    [PreserveSig] int GetProhibitedCaretStopCount(out uint count);
    [PreserveSig] int GetProhibitedCaretStops(ref uint count, IntPtr stops);
    [PreserveSig] int GetBidiLevel(out uint level);
    [PreserveSig] int GetIsSideways(out int isSideways);
    [PreserveSig] int GetDeviceFontName(out IntPtr name);
    [PreserveSig] int GetStyleSimulations(out int simulations);
    [PreserveSig] int SetStyleSimulations(int simulations);
    [PreserveSig] int GetOrigin(out XpsPoint origin);
    [PreserveSig] int SetOrigin(in XpsPoint origin);
    [PreserveSig] int GetFontRenderingEmSize(out float size);
    [PreserveSig] int SetFontRenderingEmSize(float size);
    [PreserveSig] int GetFontResource(out IntPtr font);
    [PreserveSig] int SetFontResource(IXpsOMFontResource font);
    [PreserveSig] int GetFontFaceIndex(out short index);
    [PreserveSig] int SetFontFaceIndex(short index);
    [PreserveSig] int GetFillBrush(out IntPtr brush);
    [PreserveSig] int GetFillBrushLocal(out IntPtr brush);
    [PreserveSig] int SetFillBrushLocal(IXpsOMBrush brush);
    [PreserveSig] int GetFillBrushLookup(out IntPtr key);
    [PreserveSig] int SetFillBrushLookup(IntPtr key);
    [PreserveSig] int GetGlyphsEditor(out IXpsOMGlyphsEditor editor);
    [PreserveSig] int Clone(out IntPtr glyphs);
}

/// <summary>Changes a run of glyphs takes together: its indices, its text and how they map.</summary>
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

/// <summary>A font in the package, written as it is given and obfuscated by its part's name, a GUID.</summary>
[ComImport]
[Guid("a8c45708-47d9-4af4-8d20-33b48c9b8485")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMFontResource
{
    [PreserveSig] int GetPartName(out IntPtr partUri);
    [PreserveSig] int SetPartName(IntPtr partUri);

    [PreserveSig] int GetStream(out IStream stream);

    [PreserveSig]
    int SetContent(IStream sourceStream, XpsFontEmbedding embedding,
        [MarshalAs(UnmanagedType.IUnknown)] object partUri);

    [PreserveSig] int GetEmbeddingOption(out XpsFontEmbedding embedding);
}
