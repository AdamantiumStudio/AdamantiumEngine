using System.Runtime.InteropServices;

namespace Adamantium.Win32
{
    /// <summary>POINTER_TYPE_INFO: one contact handed to <see cref="Win32Interop.InjectSyntheticPointerInput"/>, a touch or
    /// a pen as <see cref="type"/> says.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINTER_TYPE_INFO
    {
        public PointerInputType type;
        public PointerTypeInfoUnion info;

        public static POINTER_TYPE_INFO OfTouch(POINTER_TOUCH_INFO touch) =>
            new() { type = PointerInputType.Touch, info = new PointerTypeInfoUnion { touchInfo = touch } };

        public static POINTER_TYPE_INFO OfPen(POINTER_PEN_INFO pen) =>
            new() { type = PointerInputType.Pen, info = new PointerTypeInfoUnion { penInfo = pen } };
    }

    /// <summary>The union inside <see cref="POINTER_TYPE_INFO"/>: both members start at its start, and it is as large as
    /// the larger, so every element of an injected array has the size the system expects.</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct PointerTypeInfoUnion
    {
        [FieldOffset(0)]
        public POINTER_TOUCH_INFO touchInfo;

        [FieldOffset(0)]
        public POINTER_PEN_INFO penInfo;
    }
}
