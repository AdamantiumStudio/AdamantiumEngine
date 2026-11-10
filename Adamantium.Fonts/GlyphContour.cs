using System.Collections.Generic;

namespace Adamantium.Fonts
{
    /// <summary>One closed contour of a glyph's outline: its pieces in order, each starting where the one before ends,
    /// the last ending where the first starts.</summary>
    public sealed class GlyphContour
    {
        public GlyphContour(IReadOnlyList<GlyphSegment> segments)
        {
            Segments = segments;
        }

        public IReadOnlyList<GlyphSegment> Segments { get; }
    }
}
