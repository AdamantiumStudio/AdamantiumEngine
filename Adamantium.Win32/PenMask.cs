using System;

namespace Adamantium.Win32
{
    /// <summary>PEN_MASK: which of a pen's readings the device actually reports.</summary>
    [Flags]
    public enum PenMask : uint
    {
        None = 0,
        Pressure = 0x00000001,
        Rotation = 0x00000002,
        TiltX = 0x00000004,
        TiltY = 0x00000008
    }
}
