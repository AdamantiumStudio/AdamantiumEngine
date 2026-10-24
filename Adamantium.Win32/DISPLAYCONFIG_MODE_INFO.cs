using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>A source or target mode (DISPLAYCONFIG_MODE_INFO); its union is kept as raw room.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct DISPLAYCONFIG_MODE_INFO
{
    public uint InfoType;
    public uint Id;
    public uint AdapterIdLow;
    public int AdapterIdHigh;
}
