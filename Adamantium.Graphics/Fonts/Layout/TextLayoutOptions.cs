using System.Linq;
using Adamantium.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class TextLayoutOptions
{
    public int TabSize { get; init; }

    public TabStop[] TabStops { get; init; } = [];

    public TextDirection Direction { get; init; }

    public TextComposition Composition { get; init; } = TextComposition.Default;

    public Hyphens Hyphens => Composition.Hyphens;

    public int HyphenLimit => Composition.HyphenLimit;

    public LineBreaking LineBreaking => Composition.LineBreaking;

    public bool OpticalMarginAlignment => Composition.OpticalMarginAlignment;

    public DropCap DropCap { get; init; }

    public RectangleF[] Frames { get; init; }

    public RectangleF[] Exclusions { get; init; }

    public WritingMode WritingMode { get; init; }

    public SpacingRange WordSpacing => Composition.WordSpacing;

    public SpacingRange LetterSpacing => Composition.LetterSpacing;

    public SpacingRange GlyphScaling => Composition.GlyphScaling;

    public bool JustificationAlternates => Composition.JustificationAlternates;

    public HorizontalTextAlignment LastLineAlignment => Composition.LastLineAlignment;

    public HorizontalTextAlignment SingleWordJustification => Composition.SingleWordJustification;

    public bool Kashidas => Composition.Kashidas;

    public TextSpacingTrim SpacingTrim => Composition.SpacingTrim;

    public HangingPunctuation HangingPunctuation => Composition.HangingPunctuation;

    public double Tracking { get; init; }

    public TextKerning Kerning { get; init; }

    public BaselineGrid BaselineGrid { get; init; }

    public double LineHeight { get; init; } = double.NaN;

    public LineStackingStrategy LineStacking { get; init; }

    public double LineSpacing { get; init; }

    public TextLineBounds LineBounds { get; init; }

    public int MaxLines { get; init; }

    public bool BalanceLines { get; init; }

    public string Language { get; init; }

    public FontFallback Fallback { get; init; }

    public bool LoadFontsInBackground { get; init; }

    public bool EmitNewlineCarets { get; init; }

    public bool Vertical => WritingMode == WritingMode.VerticalRightToLeft;

    public bool LaysOutLike(TextLayoutOptions other)
    {
        return other != null && TabSize == other.TabSize && Direction == other.Direction
               && Composition.Equals(other.Composition) && Language == other.Language
               && TabStops.SequenceEqual(other.TabStops) && Equals(DropCap, other.DropCap)
               && WritingMode == other.WritingMode && Tracking.Equals(other.Tracking) && Kerning == other.Kerning
               && Equals(BaselineGrid, other.BaselineGrid)
               && LineHeight.Equals(other.LineHeight) && LineStacking == other.LineStacking
               && LineSpacing.Equals(other.LineSpacing) && LineBounds == other.LineBounds && MaxLines == other.MaxLines
               && BalanceLines == other.BalanceLines && (Frames ?? []).SequenceEqual(other.Frames ?? []) && (Exclusions ?? []).SequenceEqual(other.Exclusions ?? []);
    }
}
