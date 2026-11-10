namespace Adamantium.Fonts
{
    /// <summary>What a piece of a glyph's outline is.</summary>
    public enum GlyphSegmentKind
    {
        /// <summary>A straight line from its start to its end.</summary>
        Line,

        /// <summary>A quadratic Bézier: one control point, as TrueType outlines have.</summary>
        Quadratic,

        /// <summary>A cubic Bézier: two control points, as CFF outlines have.</summary>
        Cubic,
    }
}
