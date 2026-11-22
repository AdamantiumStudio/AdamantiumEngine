using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>Going through strings (Windows.Foundation.Collections.IIterator&lt;String&gt;); a string handed out is an
/// HSTRING its taker deletes. The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("8c304ebb-6615-50a4-8829-879ecd443236")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringIterator
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetCurrent(out IntPtr current);
    [PreserveSig] int GetHasCurrent([MarshalAs(UnmanagedType.U1)] out bool hasCurrent);
    [PreserveSig] int MoveNext([MarshalAs(UnmanagedType.U1)] out bool hasCurrent);
    [PreserveSig] int GetMany(uint capacity, IntPtr items, out uint count);
}
