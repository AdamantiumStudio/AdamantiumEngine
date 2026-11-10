using Adamantium.Mathematics;

namespace Adamantium.Fonts
{
    /// <summary>One piece of a glyph's outline, in font units, y up. <see cref="Control1"/> is used by quadratic and cubic
    /// pieces, <see cref="Control2"/> by cubic ones only; a line uses neither.</summary>
    public readonly record struct GlyphSegment(
        GlyphSegmentKind Kind,
        Vector2<double> Start,
        Vector2<double> Control1,
        Vector2<double> Control2,
        Vector2<double> End)
    {
        public static GlyphSegment Line(Vector2<double> start, Vector2<double> end) =>
            new(GlyphSegmentKind.Line, start, default, default, end);

        public static GlyphSegment Quadratic(Vector2<double> start, Vector2<double> control, Vector2<double> end) =>
            new(GlyphSegmentKind.Quadratic, start, control, default, end);

        public static GlyphSegment Cubic(Vector2<double> start, Vector2<double> control1, Vector2<double> control2,
            Vector2<double> end) =>
            new(GlyphSegmentKind.Cubic, start, control1, control2, end);
    }
}
