namespace Adamantium.Graphics.Core.Models;

/// <summary>Light passing through a thin surface, as glass (KHR_materials_transmission).</summary>
public class TransmissionLayer
{
    public float Factor { get; set; }

    public TextureReference Texture { get; set; }
}
