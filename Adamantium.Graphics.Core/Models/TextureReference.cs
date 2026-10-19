namespace Adamantium.Graphics.Core.Models;

/// <summary>A material's use of a texture: which one, on which texture coordinates, moved how.</summary>
public class TextureReference
{
    /// <summary>The texture's key in <see cref="SceneData.Textures"/>.</summary>
    public string Texture { get; set; }

    /// <summary>The set of texture coordinates it is read with.</summary>
    public int TexCoord { get; set; }

    /// <summary>KHR_texture_transform as the file has it; null when there is none.</summary>
    public TextureTransform Transform { get; set; }

    public ExtraData Extra { get; set; }
}
