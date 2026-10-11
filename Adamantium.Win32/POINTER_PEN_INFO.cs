using System.Runtime.InteropServices;

namespace Adamantium.Win32
{
    /// <summary>POINTER_PEN_INFO: a pen - its buttons, pressure (0 to 1024), rotation (0 to 359 degrees) and tilt
    /// (-90 to 90 degrees).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_PEN_INFO
    {
        public POINTER_INFO pointerInfo;
        public PenFlags penFlags;
        public PenMask penMask;
        public uint pressure;
        public uint rotation;
        public int tiltX;
        public int tiltY;
    }
}
