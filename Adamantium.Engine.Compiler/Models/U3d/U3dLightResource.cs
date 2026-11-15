namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A light resource (ECMA-363 9.8.1).</summary>
internal sealed class U3dLightResource
{
    internal const uint Enabled = 0x1;

    internal string Name { get; init; }

    internal uint Attributes { get; init; } = Enabled;

    /// <summary>0 ambient, 1 directional, 2 point, 3 spot.</summary>
    internal byte Type { get; init; }

    internal Vector3F Color { get; init; }

    internal float ConstantAttenuation { get; init; }

    internal float LinearAttenuation { get; init; }

    internal float QuadraticAttenuation { get; init; }

    internal float SpotAngle { get; init; }

    internal float Intensity { get; init; }
}
