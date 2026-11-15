using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A light of a scene (PRC_TYPE_GRAPH_*Light): its colors as indices of the globals' color components, and
/// for a point, directional or spot light where it is, where it shines and how it fades.</summary>
internal sealed class PrcLight
{
    internal uint Type { get; init; }

    internal string Name { get; init; }

    internal int AmbientIndex { get; init; } = -1;

    internal int DiffuseIndex { get; init; } = -1;

    internal int SpecularIndex { get; init; } = -1;

    internal Vector3<double> Location { get; init; }

    internal Vector3<double> Direction { get; init; }

    internal double ConstantAttenuation { get; init; } = 1;

    internal double LinearAttenuation { get; init; }

    internal double QuadraticAttenuation { get; init; }

    internal double Intensity { get; init; } = 1;

    /// <summary>A spot light's half angle in degrees.</summary>
    internal double FallOffAngle { get; init; }

    internal double FallOffExponent { get; init; }
}
