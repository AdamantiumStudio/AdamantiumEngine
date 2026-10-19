using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>Velvet-like back-scatter at grazing angles (KHR_materials_sheen).</summary>
public class SheenLayer
{
    public Vector3F ColorFactor { get; set; }

    public TextureReference ColorTexture { get; set; }

    public float RoughnessFactor { get; set; }

    public TextureReference RoughnessTexture { get; set; }
}
