using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>Strings to go through (Windows.Foundation.Collections.IIterable&lt;String&gt;), as a Windows Runtime call
/// takes them. The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("e2fcc7c1-3bfc-5a0b-b2b0-72e769d1cb7e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IStringIterable
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int First(out IStringIterator iterator);
}
