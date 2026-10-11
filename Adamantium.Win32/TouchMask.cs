using System;

namespace Adamantium.Win32
{
    /// <summary>TOUCH_MASK: which of a touch's readings the device actually reports.</summary>
    [Flags]
    public enum TouchMask : uint
    {
        None = 0,
        ContactArea = 0x00000001,
        Orientation = 0x00000002,
        Pressure = 0x00000004
    }
}
