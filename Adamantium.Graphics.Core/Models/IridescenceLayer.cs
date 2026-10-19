namespace Adamantium.Graphics.Core.Models;

/// <summary>A thin film's rainbow, as of soap or oil (KHR_materials_iridescence). Thicknesses in nanometres.</summary>
public class IridescenceLayer
{
    public float Factor { get; set; }

    public TextureReference Texture { get; set; }

    public float Ior { get; set; } = 1.3f;

    public float ThicknessMinimum { get; set; } = 100;

    public float ThicknessMaximum { get; set; } = 400;

    public TextureReference ThicknessTexture { get; set; }
}
