using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>How texture coordinates are moved before a texture is read: scaled, then turned by
/// <see cref="Rotation"/> radians, then offset; <see cref="TexCoord"/> over the reference's when set.</summary>
public class TextureTransform
{
    public Vector2F Offset { get; set; }

    public float Rotation { get; set; }

    public Vector2F Scale { get; set; } = Vector2F.One;

    public int? TexCoord { get; set; }
}
