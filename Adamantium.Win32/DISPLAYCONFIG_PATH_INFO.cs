using System.Runtime.InteropServices;

namespace Adamantium.Win32;

/// <summary>A path from a display source to a target (DISPLAYCONFIG_PATH_INFO), filled by
/// <see cref="Win32Interop.QueryDisplayConfig"/>; the target's refresh rate is exact, as a ratio.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_INFO
{
    public uint SourceAdapterIdLow;
    public int SourceAdapterIdHigh;
    public uint SourceId;
    public uint SourceModeInfoIndex;
    public uint SourceStatusFlags;
    public uint TargetAdapterIdLow;
    public int TargetAdapterIdHigh;
    public uint TargetId;
    public uint TargetModeInfoIndex;
    public uint OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public uint RefreshRateNumerator;
    public uint RefreshRateDenominator;
    public uint ScanLineOrdering;
    public int TargetAvailable;
    public uint TargetStatusFlags;
    public uint Flags;
}
