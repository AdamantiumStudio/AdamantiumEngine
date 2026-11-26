using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.Win32.Printing.Xps;

/// <summary>The XPS object model's factory (<c>CLSID_XpsOMObjectFactory</c>), every slot in order; those not used take
/// raw pointers.</summary>
[ComImport]
[Guid("f9b2a685-a50d-4fc2-b764-b56e093ea0ca")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IXpsOMObjectFactory
{
    [PreserveSig] int CreatePackage(out IntPtr package);
    [PreserveSig] int CreatePackageFromFile(IntPtr filename, int reuseObjects, out IntPtr package);
    [PreserveSig] int CreatePackageFromStream(IntPtr stream, int reuseObjects, out IntPtr package);
    [PreserveSig] int CreateStoryFragmentsResource(IntPtr stream, IntPtr partUri, out IntPtr resource);
    [PreserveSig] int CreateDocumentStructureResource(IntPtr stream, IntPtr partUri, out IntPtr resource);
    [PreserveSig] int CreateSignatureBlockResource(IntPtr stream, IntPtr partUri, out IntPtr resource);
    [PreserveSig] int CreateRemoteDictionaryResource(IntPtr dictionary, IntPtr partUri, out IntPtr resource);
    [PreserveSig] int CreateRemoteDictionaryResourceFromStream(IntPtr stream, IntPtr partUri, IntPtr resources,
        out IntPtr resource);
    [PreserveSig] int CreatePartResources(out IntPtr resources);
    [PreserveSig] int CreateDocumentSequence(IntPtr partUri, out IntPtr sequence);
    [PreserveSig] int CreateDocument(IntPtr partUri, out IntPtr document);
    [PreserveSig] int CreatePageReference(in XpsSize advisoryDimensions, out IntPtr reference);

    [PreserveSig]
    int CreatePage(in XpsSize dimensions, [MarshalAs(UnmanagedType.LPWStr)] string language,
        [MarshalAs(UnmanagedType.IUnknown)] object partUri, out IXpsOMPage page);

    [PreserveSig] int CreatePageFromStream(IntPtr stream, IntPtr partUri, IntPtr resources, int reuse, out IntPtr page);
    [PreserveSig] int CreateCanvas(out IXpsOMCanvas canvas);
    [PreserveSig] int CreateGlyphs(IXpsOMFontResource fontResource, out IXpsOMGlyphs glyphs);
    [PreserveSig] int CreatePath(out IXpsOMPath path);
    [PreserveSig] int CreateGeometry(out IXpsOMGeometry geometry);
    [PreserveSig] int CreateGeometryFigure(in XpsPoint start, out IXpsOMGeometryFigure figure);
    [PreserveSig] int CreateMatrixTransform(in XpsMatrix matrix, out IXpsOMMatrixTransform transform);
    [PreserveSig] int CreateSolidColorBrush(in XpsColor color, IntPtr colorProfile, out IXpsOMSolidColorBrush brush);
    [PreserveSig] int CreateColorProfileResource(IntPtr stream, IntPtr partUri, out IntPtr resource);

    [PreserveSig]
    int CreateImageBrush(IXpsOMImageResource image, in XpsRect viewbox, in XpsRect viewport,
        out IXpsOMImageBrush brush);

    [PreserveSig] int CreateVisualBrush(in XpsRect viewbox, in XpsRect viewport, out IXpsOMVisualBrush brush);

    [PreserveSig]
    int CreateImageResource(IStream acquiredStream, XpsImageType type,
        [MarshalAs(UnmanagedType.IUnknown)] object partUri, out IXpsOMImageResource resource);

    [PreserveSig]
    int CreatePrintTicketResource(IStream acquiredStream, [MarshalAs(UnmanagedType.IUnknown)] object partUri,
        out IXpsOMPrintTicketResource resource);

    /// <summary>A font of <paramref name="acquiredStream"/>, embedded as <paramref name="embedding"/> says; a stream
    /// given plain (<paramref name="isObfuscatedSourceStream"/> 0) is obfuscated as it is written.</summary>
    [PreserveSig]
    int CreateFontResource(IStream acquiredStream, XpsFontEmbedding embedding,
        [MarshalAs(UnmanagedType.IUnknown)] object partUri, int isObfuscatedSourceStream, out IXpsOMFontResource font);

    [PreserveSig]
    int CreateGradientStop(in XpsColor color, IntPtr colorProfile, float offset, out IXpsOMGradientStop stop);

    [PreserveSig]
    int CreateLinearGradientBrush(IXpsOMGradientStop first, IXpsOMGradientStop second, in XpsPoint start,
        in XpsPoint end, out IXpsOMLinearGradientBrush brush);

    [PreserveSig]
    int CreateRadialGradientBrush(IXpsOMGradientStop first, IXpsOMGradientStop second, in XpsPoint center,
        in XpsPoint origin, in XpsSize radii, out IXpsOMRadialGradientBrush brush);

    [PreserveSig] int CreateCoreProperties(IntPtr partUri, out IntPtr properties);
    [PreserveSig] int CreateDictionary(out IntPtr dictionary);
    [PreserveSig] int CreatePartUriCollection(out IntPtr collection);

    [PreserveSig]
    int CreatePackageWriterOnFile(IntPtr fileName, IntPtr security, uint flags, int optimize, XpsInterleaving interleaving,
        IntPtr sequenceName, IntPtr core, IntPtr thumbnail, IntPtr ticket, IntPtr discard, out IntPtr writer);

    /// <summary>A writer of a whole XPS package into <paramref name="output"/> - a file, or a test's memory.</summary>
    [PreserveSig]
    int CreatePackageWriterOnStream(IStream output, int optimizeMarkupSize, XpsInterleaving interleaving,
        [MarshalAs(UnmanagedType.IUnknown)] object documentSequencePartName, IntPtr coreProperties,
        IntPtr packageThumbnail, IXpsOMPrintTicketResource documentSequencePrintTicket, IntPtr discardControlPartName,
        out IXpsOMPackageWriter writer);

    /// <summary>A part's name in the package (an <c>IOpcPartUri</c>), such as "/Documents/1/Pages/1.fpage".</summary>
    [PreserveSig]
    int CreatePartUri([MarshalAs(UnmanagedType.LPWStr)] string uri,
        [MarshalAs(UnmanagedType.IUnknown)] out object partUri);

    [PreserveSig] int CreateReadOnlyStreamOnFile(IntPtr filename, out IntPtr stream);
}
