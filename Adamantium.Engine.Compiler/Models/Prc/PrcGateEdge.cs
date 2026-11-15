using System;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>An edge a triangle is reached across: its two vertices, the earlier met first, and the third vertex of
/// the triangle that pushed it.</summary>
internal readonly struct PrcGateEdge
{
    internal PrcGateEdge(int first, int second, int far)
    {
        Lower = Math.Min(first, second);
        Higher = Math.Max(first, second);
        Far = far;
    }

    internal int Lower { get; }

    internal int Higher { get; }

    internal int Far { get; }
}
