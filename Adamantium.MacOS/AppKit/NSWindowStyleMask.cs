using System;

namespace Adamantium.MacOS.AppKit;

/// <summary>What a window has: a title bar, its buttons, resizing.</summary>
[Flags]
public enum NSWindowStyleMask : ulong
{
    Borderless = 0,
    Titled = 1 << 0,
    Closable = 1 << 1,
    Miniaturizable = 1 << 2,
    Resizable = 1 << 3,
    FullSizeContentView = 1 << 15
}
