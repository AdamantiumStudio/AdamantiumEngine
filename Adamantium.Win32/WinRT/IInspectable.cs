using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>What every Windows Runtime object answers: the interfaces it has, its class name, its trust. An object of
/// ours handed to the Windows Runtime answers it too.</summary>
[ComImport]
[Guid("AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInspectable
{
    [PreserveSig] int GetIids(out uint count, out IntPtr iids);
    [PreserveSig] int GetRuntimeClassName(out IntPtr className);
    [PreserveSig] int GetTrustLevel(out int trustLevel);
}
