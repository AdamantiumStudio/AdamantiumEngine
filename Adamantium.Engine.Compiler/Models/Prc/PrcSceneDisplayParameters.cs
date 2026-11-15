using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Scene display parameters (PRC_TYPE_GRAPH_SceneDisplayParameters): the lights and camera of a scene,
/// whether it is the active one, its center of rotation, clipping planes and default styles, and whether what it
/// places is placed absolutely rather than in its sub-assembly.</summary>
internal sealed class PrcSceneDisplayParameters
{
    internal string Name { get; init; }

    internal bool IsActive { get; init; }

    internal List<PrcLight> Lights { get; } = [];

    /// <summary>Null for none.</summary>
    internal PrcCamera Camera { get; set; }

    /// <summary>Null for none.</summary>
    internal Vector3<double>? RotationCenter { get; set; }

    internal int ClippingPlaneCount { get; set; }

    internal int BackgroundStyleIndex { get; set; } = -1;

    internal bool IsAbsolute { get; set; }
}
