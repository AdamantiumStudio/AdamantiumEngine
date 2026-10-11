using System.Runtime.InteropServices;

namespace Adamantium.Win32
{
    /// <summary>POINTER_TOUCH_INFO: a finger - where it touches and how hard. Pressure runs 0 to 1024.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO pointerInfo;
        public uint touchFlags;
        public TouchMask touchMask;
        public RECT rcContact;
        public RECT rcContactRaw;
        public uint orientation;
        public uint pressure;
    }
}
