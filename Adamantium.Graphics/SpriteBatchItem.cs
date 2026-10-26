using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics;

/// <summary>
/// Describes one sprite batch item. Bound as per-instance data ([PerInstanceData]): one item = one sprite quad,
/// expanded in the vertex shader from SV_VertexID (no geometry shader).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PerInstanceData]
public struct SpriteBatchItem
{
    /// <summary>
    /// Sprite destination rectangle where sprite item will be placed
    /// </summary>
    [VertexInputElement("SV_Position")]
    public Vector4F ArrangeRect;

    /// <summary>
    /// Sprite source rectangle which texture data for rendering will be taken from
    /// </summary>
    [VertexInputElement("TEXCOORD0")]
    public Vector4F Source;

    /// <summary>
    /// Sprite origin relative to left top window corner
    /// </summary>
    [VertexInputElement("TEXCOORD1")]
    public Vector2F Origin;

    /// <summary>
    /// Sprite depth
    /// </summary>
    [VertexInputElement("PSIZE0")]
    public Single Depth;

    /// <summary>
    /// Sprite rotation
    /// </summary>
    [VertexInputElement("PSIZE1")]
    public Single Rotation;

    /// <summary>
    /// Sprite color
    /// </summary>
    [VertexInputElement("COLOR0")]
    public Color Color;

    /// <summary>The texture's width and height, 0 - or -1 for a sprite of no texture - and the texture's place in the
    /// device's texture array of no length.</summary>
    [VertexInputElement("BINORMAL0")]
    public Int4 TextureInfo;

    /// <summary>
    /// Sprite effects
    /// </summary>
    [VertexInputElement("BLENDINDICES0")]
    public int SpriteEffects;
}