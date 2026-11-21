using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32
{
    /// <summary>One input event for <see cref="Win32Interop.SendInput"/>: a key here; the mouse event is kept in the
    /// union only so the structure has the size the system expects.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public InputType Type;
        public InputUnion Data;

        /// <summary>A key going down or up, by its virtual-key code; <paramref name="extended"/> for the keys of the
        /// extended set - arrows, Home, End, Insert, Delete, page keys, the right Ctrl and Alt - which applications
        /// otherwise take for the numeric pad's.</summary>
        public static INPUT Key(ushort virtualKey, bool down, bool extended = false) => new()
        {
            Type = InputType.Keyboard,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    Flags = (down ? default : KeyEventFlags.KeyUp) | (extended ? KeyEventFlags.ExtendedKey : default),
                },
            },
        };

        /// <summary>A character going down or up, typed as it is whatever the keyboard layout.</summary>
        public static INPUT Character(char character, bool down) => new()
        {
            Type = InputType.Keyboard,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    ScanCode = character,
                    Flags = KeyEventFlags.Unicode | (down ? default : KeyEventFlags.KeyUp),
                },
            },
        };
    }

    public enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1,
        Hardware = 2,
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public KeyEventFlags Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [Flags]
    public enum KeyEventFlags : uint
    {
        ExtendedKey = 0x0001,
        KeyUp = 0x0002,
        Unicode = 0x0004,
        ScanCode = 0x0008,
    }
}
