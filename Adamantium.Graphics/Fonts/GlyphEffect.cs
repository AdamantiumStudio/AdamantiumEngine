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
/// <param name="Clip">What the range is cut by, or null. Of the effects over a glyph, the last that cuts it does.</param>
public readonly record struct GlyphEffect(int Start, int Length, Matrix3x3F Transform, bool AroundGlyph, Vector4F? Color,
    Vector2F Shift = default, GlyphClip? Clip = null)
{
    /// <summary>The character after the last one of the range.</summary>
    public int End => Start + Length;

    /// <summary>A flat transform as the matrix an effect takes.</summary>
    public static Matrix3x3F Flat(Matrix3x2F transform) => new(transform.M11, transform.M12, 0, transform.M21,
        transform.M22, 0, transform.M31, transform.M32, 1);

    /// <summary>The effect as the font shaders read it from a slot of the table a draw passes them, a chain of slots
    /// per text: <paramref name="rows"/> holds the transform in its first three rows with the start of the range, its
    /// end and <paramref name="next"/> (the slot of the text's next effect, -1 for the last) in their fourth column, and
    /// the shift, the flags (1 = around the glyph, 2 = has a color) and <paramref name="clipSlot"/> (the slot
    /// <see cref="GlyphClip.ToSlot"/> wrote <see cref="Clip"/> to, -1 for none) in the fourth row;
    /// <paramref name="color"/> is the color.</summary>
    public void ToSlot(int next, int clipSlot, out Matrix4x4F rows, out Vector4F color)
    {
        var t = Transform;
        rows = new Matrix4x4F(
            t.M11, t.M12, t.M13, Start,
            t.M21, t.M22, t.M23, End,
            t.M31, t.M32, t.M33, next,
            Shift.X, Shift.Y, (AroundGlyph ? 1 : 0) + (Color.HasValue ? 2 : 0), clipSlot);
        color = Color ?? default;
    }
}
