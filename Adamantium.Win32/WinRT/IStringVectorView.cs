using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>A read-only list of strings (Windows.Foundation.Collections.IVectorView&lt;String&gt;); each string is an
/// HSTRING the caller deletes. The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("2f13c006-a03a-5f69-b090-75a43e33423e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringVectorView
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetAt(uint index, out IntPtr item);
    [PreserveSig] int GetSize(out uint size);
}
