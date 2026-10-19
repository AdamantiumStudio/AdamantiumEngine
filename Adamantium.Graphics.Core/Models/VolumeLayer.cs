using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>The body under a transmissive surface (KHR_materials_volume).</summary>
public class VolumeLayer
{
    public float ThicknessFactor { get; set; }

    public TextureReference ThicknessTexture { get; set; }

    /// <summary>How far light goes before it takes <see cref="AttenuationColor"/>; infinity for never.</summary>
    public float AttenuationDistance { get; set; } = float.PositiveInfinity;

    public Vector3F AttenuationColor { get; set; } = Vector3F.One;
}
