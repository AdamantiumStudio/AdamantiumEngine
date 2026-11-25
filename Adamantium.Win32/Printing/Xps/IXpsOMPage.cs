using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Printing.Xps;

/// <summary>A fixed page: its size and its visuals (IXpsOMPart's slots first).</summary>
[ComImport]
[Guid("d3e18888-f120-4fee-8c68-35296eae91d4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMPage
{
    [PreserveSig] int GetPartName(out IntPtr partUri);
    [PreserveSig] int SetPartName(IntPtr partUri);

    [PreserveSig] int GetOwner(out IntPtr reference);
    [PreserveSig] int GetVisuals(out IXpsOMVisualCollection visuals);
}

/// <summary>Writes a package page by page: each page leaves memory once added.</summary>
[ComImport]
[Guid("4E2AA182-A443-42c6-B41B-4F8E9DE73FF9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMPackageWriter
{
    [PreserveSig]
    int StartNewDocument([MarshalAs(UnmanagedType.IUnknown)] object documentPartName,
        IXpsOMPrintTicketResource documentPrintTicket, IntPtr documentStructure, IntPtr signatureBlockResources,
        IntPtr restrictedFonts);

    [PreserveSig]
    int AddPage(IXpsOMPage page, in XpsSize advisoryPageDimensions, IntPtr discardableResourceParts,
        IntPtr storyFragments, IXpsOMPrintTicketResource pagePrintTicket, IntPtr pageThumbnail);

    [PreserveSig] int AddResource(IntPtr resource);

    /// <summary>Ends the package: with a print job's target, the job is then whole and the spooler prints it.</summary>
    [PreserveSig] int Close();

    [PreserveSig] int IsClosed(out int closed);
}

/// <summary>An XPS package target a print job takes its document through.</summary>
[ComImport]
[Guid("3b0b6d38-53ad-41da-b212-d37637a6714e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsDocumentPackageTarget
{
    [PreserveSig]
    int GetXpsOMPackageWriter([MarshalAs(UnmanagedType.IUnknown)] object documentSequencePartName,
        [MarshalAs(UnmanagedType.IUnknown)] object discardControlPartName, out IXpsOMPackageWriter writer);

    [PreserveSig] int GetXpsOMFactory(out IXpsOMObjectFactory factory);
}
