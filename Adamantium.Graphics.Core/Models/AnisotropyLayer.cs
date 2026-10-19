namespace Adamantium.Graphics.Core.Models;

/// <summary>Highlights drawn out along a direction, as of brushed metal (KHR_materials_anisotropy).</summary>
public class AnisotropyLayer
{
    public float Strength { get; set; }

    /// <summary>Radians, counter-clockwise from the tangent.</summary>
    public float Rotation { get; set; }

    public TextureReference Texture { get; set; }
}
