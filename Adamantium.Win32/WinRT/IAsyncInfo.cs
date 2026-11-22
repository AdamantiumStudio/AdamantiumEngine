using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>What every Windows Runtime asynchronous operation offers (Windows.Foundation.IAsyncInfo): its state, its
/// error, and canceling it. The first three methods are IInspectable's.</summary>
[ComImport]
[Guid("00000036-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAsyncInfo
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);

    [PreserveSig] int GetId(out uint id);
    [PreserveSig] int GetStatus(out AsyncStatus status);
    [PreserveSig] int GetErrorCode(out int errorCode);
    [PreserveSig] int Cancel();
    [PreserveSig] int Close();
}
