using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>An asynchronous operation that gives a list of strings
/// (Windows.Foundation.IAsyncOperation&lt;IVectorView&lt;String&gt;&gt;); it is also an <see cref="IAsyncInfo"/>. The
/// first three methods are IInspectable's.</summary>
[ComImport]
[Guid("2f92b529-119b-575a-a419-3904b4e41af2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringListOperation
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int SetCompleted(IStringListOperationHandler handler);
    [PreserveSig] int GetCompleted(out IStringListOperationHandler handler);
    [PreserveSig] int GetResults(out IStringVectorView results);
}
