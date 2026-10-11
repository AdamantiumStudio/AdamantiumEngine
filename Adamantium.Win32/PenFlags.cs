using System;

namespace Adamantium.Win32
{
    /// <summary>PEN_FLAGS: the pen's own buttons and which end of it is down.</summary>
    [Flags]
    public enum PenFlags : uint
    {
        None = 0,
        Barrel = 0x00000001,
        Inverted = 0x00000002,
        Eraser = 0x00000004
    }
}
