using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.Win32.Printing.Xps;

/// <summary>A font in the package, written as it is given and obfuscated by its part's name, a GUID. Its bases' slots
/// first (IXpsOMPart, IXpsOMResource), as xpsobjectmodel.h has them.</summary>
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
