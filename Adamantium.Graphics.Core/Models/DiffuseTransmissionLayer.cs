using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>Light passing through a thin surface, scattered as through leaves or paper
/// (KHR_materials_diffuse_transmission).</summary>
public class DiffuseTransmissionLayer
{
    public float Factor { get; set; }

    public TextureReference Texture { get; set; }

    public Vector3F ColorFactor { get; set; } = Vector3F.One;

    public TextureReference ColorTexture { get; set; }
}
