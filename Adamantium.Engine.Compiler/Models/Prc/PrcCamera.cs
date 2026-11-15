using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A camera (PRC_TYPE_GRAPH_Camera): where it is, what it looks at, which way is up, its field of view in
/// radians - or for an orthographic one its scale - its aspect, clipping distances and zoom.</summary>
internal sealed class PrcCamera
{
    internal string Name { get; init; }

    internal bool IsOrthographic { get; init; }

    internal Vector3<double> Position { get; init; }

    internal Vector3<double> LookAt { get; init; }

    internal Vector3<double> Up { get; init; }

    internal double FieldOfViewX { get; init; }

    internal double FieldOfViewY { get; init; }

    internal double Aspect { get; init; }

    internal double Near { get; init; }

    internal double Far { get; init; }

    internal double Zoom { get; init; }
}
