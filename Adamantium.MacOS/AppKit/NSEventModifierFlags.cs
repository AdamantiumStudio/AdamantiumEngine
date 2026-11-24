using System;

namespace Adamantium.MacOS.AppKit;

/// <summary>The modifier keys held during an event.</summary>
[Flags]
public enum NSEventModifierFlags : ulong
{
    CapsLock = 1 << 16,
    Shift = 1 << 17,
    Control = 1 << 18,
    Option = 1 << 19,
    Command = 1 << 20,
    NumericPad = 1 << 21,
    Function = 1 << 23
}
