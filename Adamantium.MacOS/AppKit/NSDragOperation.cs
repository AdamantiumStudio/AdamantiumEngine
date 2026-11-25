using System;

namespace Adamantium.MacOS.AppKit;

/// <summary>What a drag does with what it carries.</summary>
[Flags]
public enum NSDragOperation : ulong
{
    None = 0,
    Copy = 1,
    Link = 2,

    /// <summary>Whatever the target takes it for; what is left with Command held.</summary>
    Generic = 4,

    Private = 8,
    Move = 16,
    Delete = 32
}
