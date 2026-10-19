using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>The strength and color of a material's specular reflection (KHR_materials_specular).</summary>
public class SpecularLayer
{
    public float Factor { get; set; } = 1;

    public TextureReference Texture { get; set; }

    public Vector3F ColorFactor { get; set; } = Vector3F.One;

    public TextureReference ColorTexture { get; set; }
}
