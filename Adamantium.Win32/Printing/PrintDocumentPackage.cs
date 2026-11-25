using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.Win32.Printing;

/// <summary>Print Document Package (Windows 8 and later): a print job a document is written into as XPS, with the job's
/// PrintTicket.</summary>
public static class PrintDocumentPackage
{
    /// <summary><c>CLSID_PrintDocumentPackageTargetFactory</c>.</summary>
    public static readonly Guid TargetFactoryClass = new("348ef17d-6c81-4982-92b4-ee188a43867a");

    /// <summary><c>ID_DOCUMENTPACKAGETARGET_MSXPS</c>: the target that takes Microsoft XPS through the object model.
    /// </summary>
    public static readonly Guid MsXpsTarget = new("9cae40a8-ded1-41c9-a9fd-d735ef33aeda");

    /// <summary><c>CLSID_XpsOMObjectFactory</c>.</summary>
    public static readonly Guid XpsObjectFactoryClass = new("E974D26D-3D9B-4D47-88CC-3872F2DC3585");
}

[ComImport]
[Guid("d2959bf7-b31b-4a3d-9600-712eb1335ba4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPrintDocumentPackageTargetFactory
{
    /// <summary>A job on <paramref name="printerName"/>; <paramref name="jobOutputStream"/>, when given, takes what the
    /// driver makes instead of the device - a file printed to.</summary>
    [PreserveSig]
    int CreateDocumentPackageTargetForPrintJob([MarshalAs(UnmanagedType.LPWStr)] string printerName,
        [MarshalAs(UnmanagedType.LPWStr)] string jobName, IStream jobOutputStream, IStream jobPrintTicketStream,
        out IPrintDocumentPackageTarget target);
}

[ComImport]
[Guid("1b8efec4-3019-4c27-964e-367202156906")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPrintDocumentPackageTarget
{
    [PreserveSig] int GetPackageTargetTypes(out uint count, out IntPtr types);

    [PreserveSig]
    int GetPackageTarget(in Guid targetType, in Guid interfaceId,
        [MarshalAs(UnmanagedType.IUnknown)] out object target);

    /// <summary>Calls the job off.</summary>
    [PreserveSig] int Cancel();
}
