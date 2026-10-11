using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Win32
{
    /// <summary>POINTER_INFO: what every pointer reports, whatever its kind.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_INFO
    {
        public PointerInputType pointerType;
        public uint pointerId;
        public uint frameId;
        public PointerFlags pointerFlags;
        public IntPtr sourceDevice;
        public IntPtr hwndTarget;
        public NativePoint ptPixelLocation;
        public NativePoint ptHimetricLocation;
        public NativePoint ptPixelLocationRaw;
        public NativePoint ptHimetricLocationRaw;
        public uint dwTime;
        public uint historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public PointerButtonChangeType ButtonChangeType;
    }
}
