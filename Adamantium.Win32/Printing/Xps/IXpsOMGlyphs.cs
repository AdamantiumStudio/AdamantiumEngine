using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Printing.Xps;

/// <summary>A run of glyphs of one font at one size, from an origin, filled by a brush. Its bases' slots first
/// (IXpsOMShareable, then IXpsOMVisual), as xpsobjectmodel.h has them; slots not used take raw pointers.</summary>
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
