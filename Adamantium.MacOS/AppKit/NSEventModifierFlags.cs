using System;

namespace Adamantium.MacOS.AppKit;

/// <summary>The modifier keys held during an event.</summary>
[Flags]
public enum NSEventModifierFlags : ulong
{
    LeftControl = 1 << 0,
    LeftShift = 1 << 1,
    RightShift = 1 << 2,
    LeftCommand = 1 << 3,
    RightCommand = 1 << 4,
    LeftOption = 1 << 5,
    RightOption = 1 << 6,
    RightControl = 1 << 13,
    CapsLock = 1 << 16,
    Shift = 1 << 17,
    Control = 1 << 18,
    Option = 1 << 19,
    Command = 1 << 20,
    NumericPad = 1 << 21,
    Function = 1 << 23
}
