using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A rounded rectangle a <see cref="GlyphEffect"/> cuts its glyphs by: what of a glyph lies outside it is not
/// drawn.</summary>
/// <param name="Rect">The rectangle, in its own space.</param>
/// <param name="Radii">The corners' radii: top left, top right, bottom right, bottom left.</param>
/// <param name="ToClip">From the layout's units into the rectangle's space, as a point times the matrix.</param>
/// <param name="AfterTransform">Whether the glyphs are cut where the effects over them put them, so they pass through a
/// rectangle that stays; otherwise where the layout put them, and the rectangle goes with them.</param>
public readonly record struct GlyphClip(RectangleF Rect, Vector4F Radii, Matrix3x2F ToClip, bool AfterTransform)
{
    /// <summary>The clip as the font shaders read it from a slot (see <see cref="GlyphEffect.ToSlot"/>): the rectangle,
    /// the radii, <see cref="ToClip"/> as m11 m12 m21 m22 and then m31 m32 with "after the transform".</summary>
    public void ToSlot(out Matrix4x4F rows, out Vector4F rest)
    {
        var m = ToClip;
        rows = new Matrix4x4F(
            Rect.X, Rect.Y, Rect.Width, Rect.Height,
            Radii.X, Radii.Y, Radii.Z, Radii.W,
            m.M11, m.M12, m.M21, m.M22,
            m.M31, m.M32, AfterTransform ? 1 : 0, 0);
        rest = default;
    }
}
