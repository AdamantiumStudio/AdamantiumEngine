using System;

namespace Adamantium.MacOS.AppKit;

/// <summary>Where a gesture on a trackpad is.</summary>
[Flags]
public enum NSEventPhase : ulong
{
    None = 0,
    Began = 1,
    Stationary = 2,
    Changed = 4,
    Ended = 8,
    Cancelled = 16,
    MayBegin = 32
}
