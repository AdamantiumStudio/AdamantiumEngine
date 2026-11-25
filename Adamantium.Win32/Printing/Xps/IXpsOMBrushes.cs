using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.Win32.Printing.Xps;

// Brushes and the resources they paint with. Each interface lists its bases' slots first, as its vtable has them.

/// <summary>Any brush - what a fill, a stroke or an opacity mask is set to.</summary>
[ComImport]
[Guid("56A3F80C-EA4C-4187-A57B-A2A473B2B42B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMBrush
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);
}

[ComImport]
[Guid("A06F9F05-3BE9-4763-98A8-094FC672E488")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMSolidColorBrush
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);

    [PreserveSig] int GetColor(out XpsColor color, out IntPtr colorProfile);
    [PreserveSig] int SetColor(in XpsColor color, IntPtr colorProfile);
}

/// <summary>A brush that paints a picture, tiled or not: an image brush's slots up to its own.</summary>
[ComImport]
[Guid("3DF0B466-D382-49EF-8550-DD94C80242E4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMImageBrush
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);

    [PreserveSig] int GetTransform(out IntPtr transform);
    [PreserveSig] int GetTransformLocal(out IntPtr transform);
    [PreserveSig] int SetTransformLocal(IXpsOMMatrixTransform transform);
    [PreserveSig] int GetTransformLookup(out IntPtr key);
    [PreserveSig] int SetTransformLookup(IntPtr key);
    [PreserveSig] int GetViewbox(out XpsRect viewbox);
    [PreserveSig] int SetViewbox(in XpsRect viewbox);
    [PreserveSig] int GetViewport(out XpsRect viewport);
    [PreserveSig] int SetViewport(in XpsRect viewport);
    [PreserveSig] int GetTileMode(out XpsTileMode mode);
    [PreserveSig] int SetTileMode(XpsTileMode mode);
}

/// <summary>A brush that paints visuals, tiled or not.</summary>
[ComImport]
[Guid("97E294AF-5B37-46B4-8057-874D2F64119B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMVisualBrush
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);

    [PreserveSig] int GetTransform(out IntPtr transform);
    [PreserveSig] int GetTransformLocal(out IntPtr transform);
    [PreserveSig] int SetTransformLocal(IXpsOMMatrixTransform transform);
    [PreserveSig] int GetTransformLookup(out IntPtr key);
    [PreserveSig] int SetTransformLookup(IntPtr key);
    [PreserveSig] int GetViewbox(out XpsRect viewbox);
    [PreserveSig] int SetViewbox(in XpsRect viewbox);
    [PreserveSig] int GetViewport(out XpsRect viewport);
    [PreserveSig] int SetViewport(in XpsRect viewport);
    [PreserveSig] int GetTileMode(out XpsTileMode mode);
    [PreserveSig] int SetTileMode(XpsTileMode mode);

    [PreserveSig] int GetVisual(out IntPtr visual);
    [PreserveSig] int GetVisualLocal(out IntPtr visual);
    [PreserveSig] int SetVisualLocal(IXpsOMVisual visual);
}

[ComImport]
[Guid("5CF4F5CC-3969-49B5-A70A-5550B618FE49")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGradientStop
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetOffset(out float offset);
    [PreserveSig] int SetOffset(float offset);
    [PreserveSig] int GetColor(out XpsColor color, out IntPtr colorProfile);
    [PreserveSig] int SetColor(in XpsColor color, IntPtr colorProfile);
}

[ComImport]
[Guid("C9174C3A-3CD3-4319-BDA4-11A39392CEEF")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMGradientStopCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out IntPtr stop);
    [PreserveSig] int InsertAt(uint index, IXpsOMGradientStop stop);
    [PreserveSig] int RemoveAt(uint index);
    [PreserveSig] int SetAt(uint index, IXpsOMGradientStop stop);
    [PreserveSig] int Append(IXpsOMGradientStop stop);
}

/// <summary>A linear gradient: a gradient brush's slots, then its own.</summary>
[ComImport]
[Guid("005E279F-C30D-40FF-93EC-1950D3C528DB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMLinearGradientBrush
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);

    [PreserveSig] int GetGradientStops(out IXpsOMGradientStopCollection stops);
    [PreserveSig] int GetTransform(out IntPtr transform);
    [PreserveSig] int GetTransformLocal(out IntPtr transform);
    [PreserveSig] int SetTransformLocal(IXpsOMMatrixTransform transform);
    [PreserveSig] int GetTransformLookup(out IntPtr key);
    [PreserveSig] int SetTransformLookup(IntPtr key);
    [PreserveSig] int GetSpreadMethod(out XpsSpreadMethod spread);
    [PreserveSig] int SetSpreadMethod(XpsSpreadMethod spread);
    [PreserveSig] int GetColorInterpolationMode(out XpsColorInterpolation mode);
    [PreserveSig] int SetColorInterpolationMode(XpsColorInterpolation mode);
}

/// <summary>A radial gradient: an ellipse around a center, from an origin inside it.</summary>
[ComImport]
[Guid("75F207E5-08BF-413C-96B1-B82B4064176B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMRadialGradientBrush
{
    [PreserveSig] int GetOwner(out IntPtr owner);
    [PreserveSig] int GetType(out int type);

    [PreserveSig] int GetOpacity(out float opacity);
    [PreserveSig] int SetOpacity(float opacity);

    [PreserveSig] int GetGradientStops(out IXpsOMGradientStopCollection stops);
    [PreserveSig] int GetTransform(out IntPtr transform);
    [PreserveSig] int GetTransformLocal(out IntPtr transform);
    [PreserveSig] int SetTransformLocal(IXpsOMMatrixTransform transform);
    [PreserveSig] int GetTransformLookup(out IntPtr key);
    [PreserveSig] int SetTransformLookup(IntPtr key);
    [PreserveSig] int GetSpreadMethod(out XpsSpreadMethod spread);
    [PreserveSig] int SetSpreadMethod(XpsSpreadMethod spread);
    [PreserveSig] int GetColorInterpolationMode(out XpsColorInterpolation mode);
    [PreserveSig] int SetColorInterpolationMode(XpsColorInterpolation mode);
}

/// <summary>A picture in the package - PNG, JPEG, TIFF or JPEG XR.</summary>
[ComImport]
[Guid("3db8417d-ae50-485e-9a44-d7758f78a23f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMImageResource
{
    [PreserveSig] int GetPartName(out IntPtr partUri);
    [PreserveSig] int SetPartName(IntPtr partUri);

    [PreserveSig] int GetStream(out IStream stream);
}

/// <summary>A PrintTicket in the package: the job's settings, as the printer reads them.</summary>
[ComImport]
[Guid("e7ff32d2-34aa-499b-bbe9-9cd4ee6c59f7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMPrintTicketResource
{
    [PreserveSig] int GetPartName(out IntPtr partUri);
    [PreserveSig] int SetPartName(IntPtr partUri);

    [PreserveSig] int GetStream(out IStream stream);
}
