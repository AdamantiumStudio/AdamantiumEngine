using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>
/// Describes one glyph batch item. Bound as per-instance data ([PerInstanceData]): one item = one glyph quad,
/// expanded in the vertex shader from SV_VertexID (no geometry shader).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PerInstanceData]
public struct FontItem
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
    public Vector4F Color;

    /// <summary>
    /// Sprite effects
    /// </summary>
    [VertexInputElement("BLENDINDICES0")]
    public int SpriteEffects;

    /// <summary>
    /// Atlas array LAYER: which Texture2DArray slice this glyph's UVs live in. The vertex shader passes it as UV.z so
    /// the pixel shader samples the right slice. Kept as float for a portable vertex attribute.
    /// </summary>
    [VertexInputElement("PSIZE2")]
    public Single Layer;

    /// <summary>
    /// What draws the glyph as a face its font lacks (<see cref="Adamantium.Fonts.FontSynthesis"/>): x = how far the
    /// outline moves out for a bold, in units of the distance field; y = the slant of an italic; z = where the baseline
    /// lies in the quad, 0 at its top and 1 at its bottom, the line the slant pivots on; w = the width of a soft edge,
    /// in units of the distance field, for a glyph drawn as a shadow or a glow (0 for a crisp one). Zero draws the
    /// glyph as it is.
    /// </summary>
    [VertexInputElement("TEXCOORD2")]
    public Vector4F Synthesis;

    /// <summary>
    /// A 'COLR' version 1 glyph, drawn by its paint program: x = the program in the atlas plus one, 0 for an ordinary
    /// glyph. Such a glyph's <see cref="ArrangeRect"/> holds where its pen and baseline are and the pixels per font unit,
    /// as x, y, z (and again w): the program places the quad. A glyph drawn as a reflection: z = the share of the quad,
    /// from its top, it fades away over; w = how strong it starts; zero for every other glyph.
    /// </summary>
    [VertexInputElement("TEXCOORD3")]
    public Vector4F Paint;

    /// <summary>
    /// A glyph of a font on the way between two key instances (<see cref="Adamantium.Fonts.FontBlend"/>): the second
    /// key's cell over the same quad as <see cref="Source"/>, as u, v, width and height; zero for a glyph drawn from one
    /// field.
    /// </summary>
    [VertexInputElement("TEXCOORD4")]
    public Vector4F SecondSource;

    /// <summary>
    /// x = the atlas layer of <see cref="SecondSource"/>; y = how far the glyph is from the first key to the second. Such
    /// a glyph is drawn from the true distance of both fields, blended.
    /// </summary>
    [VertexInputElement("TEXCOORD5")]
    public Vector2F Second;

    /// <summary>
    /// The cell the layout gave the glyph, as x, y, width and height: what a <see cref="GlyphEffect"/> turns the glyph
    /// about and sizes its <see cref="GlyphEffect.Shift"/> by.
    /// </summary>
    [VertexInputElement("TEXCOORD6")]
    public Vector4F Cell;

    /// <summary>
    /// x = the UTF-16 character the glyph draws, which decides the effects that reach it, or -1 for a glyph no effect
    /// moves; y = 1 for a glyph in the text's color, which an effect's color replaces, 0 for one in a color of its own.
    /// </summary>
    [VertexInputElement("TEXCOORD7")]
    public Vector2F Place;

    /// <summary>The <see cref="Color"/> of a glyph that takes the element's foreground: a negative alpha.</summary>
    public static readonly Vector4F InheritedColor = new(0, 0, 0, -1);

    /// <summary>Whether the glyph has a color of its own rather than the element's foreground.</summary>
    public bool HasOwnColor => Color.W >= 0;
}