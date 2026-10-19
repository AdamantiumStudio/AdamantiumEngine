namespace Adamantium.Graphics.Core.Models;

/// <summary>A clear lacquer over a material (KHR_materials_clearcoat).</summary>
public class ClearcoatLayer
{
    public float Factor { get; set; }

    public TextureReference Texture { get; set; }

    public float RoughnessFactor { get; set; }

    public TextureReference RoughnessTexture { get; set; }

    public TextureReference NormalTexture { get; set; }

    public float NormalScale { get; set; } = 1;
}
