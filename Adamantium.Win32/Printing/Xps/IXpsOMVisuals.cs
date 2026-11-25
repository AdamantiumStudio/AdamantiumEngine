using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Printing.Xps;

// The visual tree of an XPS page. Each interface lists its bases' slots first (IXpsOMShareable, then IXpsOMVisual),
// as its vtable has them; slots not used take raw pointers.

/// <summary>A visual: a path, a canvas or glyphs - placed, cut, faded and masked.</summary>
[ComImport]
[Guid("BC3E7333-FB0B-4af3-A819-0B4EAAD0D2FD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMVisual
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
}

/// <summary>The visuals of a page or a canvas, in drawing order.</summary>
[ComImport]
[Guid("94D8ABDE-AB91-46A8-82B7-F5B05EF01A96")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMVisualCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out IntPtr visual);
    [PreserveSig] int InsertAt(uint index, IXpsOMVisual visual);
    [PreserveSig] int RemoveAt(uint index);
    [PreserveSig] int SetAt(uint index, IXpsOMVisual visual);
    [PreserveSig] int Append(IXpsOMVisual visual);
}

/// <summary>A group of visuals with one placement, clip, opacity and opacity mask.</summary>
[ComImport]
[Guid("221D1452-331E-47c6-87E9-6CCEFB9B5BA3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMCanvas
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

    [PreserveSig] int GetVisuals(out IXpsOMVisualCollection visuals);
}

/// <summary>A shape: a geometry filled and stroked.</summary>
[ComImport]
[Guid("37D38BB6-3EE9-4110-9312-14B194163337")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMPath
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

    [PreserveSig] int GetGeometry(out IntPtr geometry);
    [PreserveSig] int GetGeometryLocal(out IntPtr geometry);
    [PreserveSig] int SetGeometryLocal(IXpsOMGeometry geometry);
    [PreserveSig] int GetGeometryLookup(out IntPtr key);
    [PreserveSig] int SetGeometryLookup(IntPtr key);
    [PreserveSig] int GetAccessibilityShortDescription(out IntPtr text);
    [PreserveSig] int SetAccessibilityShortDescription(IntPtr text);
    [PreserveSig] int GetAccessibilityLongDescription(out IntPtr text);
    [PreserveSig] int SetAccessibilityLongDescription(IntPtr text);
    [PreserveSig] int GetSnapsToPixels(out int snaps);
    [PreserveSig] int SetSnapsToPixels(int snaps);
    [PreserveSig] int GetStrokeBrush(out IntPtr brush);
    [PreserveSig] int GetStrokeBrushLocal(out IntPtr brush);
    [PreserveSig] int SetStrokeBrushLocal(IXpsOMBrush brush);
    [PreserveSig] int GetStrokeBrushLookup(out IntPtr key);
    [PreserveSig] int SetStrokeBrushLookup(IntPtr key);
    [PreserveSig] int GetStrokeDashes(out IXpsOMDashCollection dashes);
    [PreserveSig] int GetStrokeDashCap(out XpsDashCap cap);
    [PreserveSig] int SetStrokeDashCap(XpsDashCap cap);
    [PreserveSig] int GetStrokeDashOffset(out float offset);
    [PreserveSig] int SetStrokeDashOffset(float offset);
    [PreserveSig] int GetStrokeStartLineCap(out XpsLineCap cap);
    [PreserveSig] int SetStrokeStartLineCap(XpsLineCap cap);
    [PreserveSig] int GetStrokeEndLineCap(out XpsLineCap cap);
    [PreserveSig] int SetStrokeEndLineCap(XpsLineCap cap);
    [PreserveSig] int GetStrokeLineJoin(out XpsLineJoin join);
    [PreserveSig] int SetStrokeLineJoin(XpsLineJoin join);
    [PreserveSig] int GetStrokeMiterLimit(out float limit);
    [PreserveSig] int SetStrokeMiterLimit(float limit);
    [PreserveSig] int GetStrokeThickness(out float thickness);
    [PreserveSig] int SetStrokeThickness(float thickness);
    [PreserveSig] int GetFillBrush(out IntPtr brush);
    [PreserveSig] int GetFillBrushLocal(out IntPtr brush);
    [PreserveSig] int SetFillBrushLocal(IXpsOMBrush brush);
}

/// <summary>A pen's dashes, as lengths and gaps in units of its width.</summary>
[ComImport]
[Guid("081613F4-74EB-48F2-83B3-37A9CE2D7DC6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMDashCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out XpsDash dash);
    [PreserveSig] int InsertAt(uint index, in XpsDash dash);
    [PreserveSig] int RemoveAt(uint index);
    [PreserveSig] int SetAt(uint index, in XpsDash dash);
    [PreserveSig] int Append(in XpsDash dash);
}

/// <summary>A placement: a 2D affine matrix.</summary>
[ComImport]
[Guid("B77330FF-BB37-4501-A93E-F1B1E50BFC46")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMMatrixTransform
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetMatrix(out XpsMatrix matrix);
    [PreserveSig] int SetMatrix(in XpsMatrix matrix);
}

/// <summary>A shape's outline: figures under a fill rule.</summary>
[ComImport]
[Guid("64FCF3D7-4D58-44BA-AD73-A13AF6492072")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGeometry
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetFigures(out IXpsOMGeometryFigureCollection figures);
    [PreserveSig] int GetFillRule(out XpsFillRule rule);
    [PreserveSig] int SetFillRule(XpsFillRule rule);
    [PreserveSig] int GetTransform(out IntPtr transform);
    [PreserveSig] int GetTransformLocal(out IntPtr transform);
    [PreserveSig] int SetTransformLocal(IXpsOMMatrixTransform transform);
}

/// <summary>One figure of a geometry: a start point and its segments.</summary>
[ComImport]
[Guid("D410DC83-908C-443E-8947-B1795D3C165A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGeometryFigure
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetSegmentData(ref uint count, IntPtr data);
    [PreserveSig] int GetSegmentTypes(ref uint count, IntPtr types);
    [PreserveSig] int GetSegmentStrokes(ref uint count, IntPtr strokes);

    /// <summary>All the segments at once: their types, their numbers (two a line, four a quadratic Bézier, six a cubic
    /// one), and whether each is stroked.</summary>
    [PreserveSig]
    int SetSegments(uint segmentCount, uint dataCount, [In, MarshalAs(UnmanagedType.LPArray)] XpsSegmentType[] types,
        [In, MarshalAs(UnmanagedType.LPArray)] float[] data, [In, MarshalAs(UnmanagedType.LPArray)] int[] strokes);

    [PreserveSig] int GetStartPoint(out XpsPoint start);
    [PreserveSig] int SetStartPoint(in XpsPoint start);
    [PreserveSig] int GetIsClosed(out int closed);
    [PreserveSig] int SetIsClosed(int closed);
    [PreserveSig] int GetIsFilled(out int filled);
    [PreserveSig] int SetIsFilled(int filled);
}

[ComImport]
[Guid("FD48C3F3-A58E-4B5A-8826-1DE54ABE72B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGeometryFigureCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out IntPtr figure);
    [PreserveSig] int InsertAt(uint index, IXpsOMGeometryFigure figure);
    [PreserveSig] int RemoveAt(uint index);
    [PreserveSig] int SetAt(uint index, IXpsOMGeometryFigure figure);
    [PreserveSig] int Append(IXpsOMGeometryFigure figure);
}
