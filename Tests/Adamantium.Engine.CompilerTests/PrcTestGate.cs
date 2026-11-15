using System;

namespace Adamantium.Engine.CompilerTests;

/// <summary>An edge of the test traversal: its two points by index, the earlier first, and the third point of the
/// triangle that pushed it.</summary>
internal sealed class PrcTestGate
{
    public PrcTestGate(int first, int second, int far)
    {
        Lower = Math.Min(first, second);
        Higher = Math.Max(first, second);
        Far = far;
    }

    public int Lower { get; }

    public int Higher { get; }

    public int Far { get; }
}
