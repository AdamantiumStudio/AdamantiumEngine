using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>The older specular-glossiness workflow of a material (KHR_materials_pbrSpecularGlossiness).</summary>
public class SpecularGlossiness
{
    public Vector4F DiffuseFactor { get; set; } = Vector4F.One;

    public TextureReference DiffuseTexture { get; set; }

    public Vector3F SpecularFactor { get; set; } = Vector3F.One;

    public float GlossinessFactor { get; set; } = 1;

    public TextureReference SpecularGlossinessTexture { get; set; }
}
