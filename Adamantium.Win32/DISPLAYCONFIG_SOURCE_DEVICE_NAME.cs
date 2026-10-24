using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>A display source's GDI device name (DISPLAYCONFIG_SOURCE_DEVICE_NAME), asked for with
/// <see cref="Win32Interop.DisplayConfigGetDeviceInfo"/>.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    /// <summary>DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME in <see cref="Type"/>.</summary>
    public const uint GetSourceName = 1;

    public uint Type;
    public uint Size;
    public uint AdapterIdLow;
    public int AdapterIdHigh;
    public uint Id;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string GdiDeviceName;
}
