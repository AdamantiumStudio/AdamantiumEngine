using System.Linq;
using Adamantium.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class TextLayoutOptions
{
    public int TabSize { get; init; }

    public TabStop[] TabStops { get; init; } = [];

    public TextDirection Direction { get; init; }

    public Hyphens Hyphens { get; init; }

    public LineBreaking LineBreaking { get; init; }

    public bool OpticalMarginAlignment { get; init; }

    public DropCap DropCap { get; init; }

    public RectangleF[] Frames { get; init; }

    public RectangleF[] Exclusions { get; init; }

    public WritingMode WritingMode { get; init; }

    public SpacingRange WordSpacing { get; init; }

    public SpacingRange LetterSpacing { get; init; }

    public SpacingRange GlyphScaling { get; init; }

    public bool JustificationAlternates { get; init; }

    public HorizontalTextAlignment LastLineAlignment { get; init; }

    public HorizontalTextAlignment SingleWordJustification { get; init; }

    public bool Kashidas { get; init; }

    public double Tracking { get; init; }

    public TextKerning Kerning { get; init; }

    public BaselineGrid BaselineGrid { get; init; }

    public double LineHeight { get; init; } = double.NaN;

    public LineStackingStrategy LineStacking { get; init; }

    public double LineSpacing { get; init; }

    public int MaxLines { get; init; }

    public string Language { get; init; }

    public FontFallback Fallback { get; init; }

    public bool LoadFontsInBackground { get; init; }

    public bool EmitNewlineCarets { get; init; }

    public bool Vertical => WritingMode == WritingMode.VerticalRightToLeft;

    public bool LaysOutLike(TextLayoutOptions other)
    {
        return other != null && TabSize == other.TabSize && Direction == other.Direction && Hyphens == other.Hyphens
               && Language == other.Language && LineBreaking == other.LineBreaking
               && TabStops.SequenceEqual(other.TabStops) && OpticalMarginAlignment == other.OpticalMarginAlignment
               && Equals(DropCap, other.DropCap) && WritingMode == other.WritingMode
               && WordSpacing.Equals(other.WordSpacing) && LetterSpacing.Equals(other.LetterSpacing)
               && Tracking.Equals(other.Tracking) && Kerning == other.Kerning
               && Equals(BaselineGrid, other.BaselineGrid)
               && GlyphScaling.Equals(other.GlyphScaling)
               && JustificationAlternates == other.JustificationAlternates
               && LastLineAlignment == other.LastLineAlignment
               && SingleWordJustification == other.SingleWordJustification && Kashidas == other.Kashidas
               && LineHeight.Equals(other.LineHeight) && LineStacking == other.LineStacking
               && LineSpacing.Equals(other.LineSpacing) && MaxLines == other.MaxLines
               && (Frames ?? []).SequenceEqual(other.Frames ?? []) && (Exclusions ?? []).SequenceEqual(other.Exclusions ?? []);
    }
}
