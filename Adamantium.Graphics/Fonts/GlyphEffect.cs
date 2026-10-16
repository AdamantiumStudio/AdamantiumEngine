using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>How a range of the text is drawn apart from the rest: its glyphs moved by a transform and, when given, drawn
/// in a color of their own. Only the drawing changes; the lines, the caret and hit testing stay where the layout put
/// them.</summary>
/// <param name="Start">The first UTF-16 character of the range, in the text laid out.</param>
/// <param name="Length">How many characters the range takes.</param>
/// <param name="Transform">The transform, in the layout's units, as a point times the matrix, [x y 1]: about the center of
/// each glyph when <paramref name="AroundGlyph"/>, else about the layout's origin. Its third column is a perspective: a
/// glyph turned away from the viewer narrows toward its far side. Effects over the same glyph apply in their
/// order.</param>
/// <param name="AroundGlyph">Whether each glyph turns and scales about its own center.</param>
/// <param name="Color">Straight RGBA the range is drawn in instead of the text's color, or null; a glyph's own colors,
/// such as an emoji's, stay.</param>
/// <param name="Shift">A move after the transform, in sizes of each glyph: (0, -1) raises every glyph by its own
/// height.</param>
public readonly record struct GlyphEffect(int Start, int Length, Matrix3x3F Transform, bool AroundGlyph, Vector4F? Color,
    Vector2F Shift = default)
{
    /// <summary>The character after the last one of the range.</summary>
    public int End => Start + Length;

    /// <summary>A flat transform as the matrix an effect takes.</summary>
    public static Matrix3x3F Flat(Matrix3x2F transform) => new(transform.M11, transform.M12, 0, transform.M21,
        transform.M22, 0, transform.M31, transform.M32, 1);
}
