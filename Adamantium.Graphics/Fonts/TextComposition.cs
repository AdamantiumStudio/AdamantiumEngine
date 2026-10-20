using System;

namespace Adamantium.Graphics.Fonts;

/// <summary>How the lines of a text are composed, as InDesign's paragraph settings: where words break, how a justified
/// line fills its width, and how CJK punctuation is set. Compared by value, so whoever lays text out can tell at once
/// whether any of it changed.</summary>
public sealed record TextComposition
{
    private readonly int _hyphenLimit;
    private readonly SpacingRange _glyphScaling = new(1, 1, 1);

    /// <summary>Every setting at its default.</summary>
    public static TextComposition Default { get; } = new();

    /// <summary>
    /// Where words wrapped by <see cref="TextWrapping.WrapByWords"/> may break across lines:
    /// <see cref="Fonts.Hyphens.Manual"/> (the default) at soft hyphens only.
    /// </summary>
    public Hyphens Hyphens { get; init; } = Hyphens.Manual;

    /// <summary>
    /// The most lines in a row that may end in a hyphen breaking a word, as InDesign's hyphen limit: past it a word is
    /// not broken but goes down whole, unless it is the only word on its line. Hyphens of the text itself, as in
    /// "state-of-the-art", are not counted. 0, the default, sets no limit.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0.</exception>
    public int HyphenLimit
    {
        get => _hyphenLimit;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _hyphenLimit = value;
        }
    }

    /// <summary>
    /// How words wrapped by <see cref="TextWrapping.WrapByWords"/> are broken into lines: a line at a time
    /// (<see cref="Fonts.LineBreaking.Greedy"/>, the default) or a paragraph at a time.
    /// </summary>
    public LineBreaking LineBreaking { get; init; } = LineBreaking.Greedy;

    /// <summary>
    /// Hangs punctuation and hyphens at the edges of lines partly past the margin, as InDesign's Optical Margin
    /// Alignment does, so the text's edge looks straight: the start of lines aligned left or justified, the end of
    /// lines aligned right or justified. Lines of right-to-left paragraphs and centered lines are left as they are.
    /// False by default.
    /// </summary>
    public bool OpticalMarginAlignment { get; init; }

    /// <summary>
    /// How wide the spaces between words are, as shares of the font's space: their width as laid out, and how far a
    /// justified line may squeeze and stretch them (InDesign's 80%, 100% and 133% by default). A justified line that
    /// cannot fill its width within the range takes letter spacing next, then stretches its spaces further.
    /// </summary>
    public SpacingRange WordSpacing { get; init; } = SpacingRange.Words;

    /// <summary>
    /// What is added between letters, as shares of the font's space: the width added as laid out, and how far a
    /// justified line may take it below and above that, once its spaces are at their limits. None by default. Cursive
    /// scripts, such as Arabic, are never spaced.
    /// </summary>
    public SpacingRange LetterSpacing { get; init; } = SpacingRange.Letters;

    /// <summary>
    /// How wide glyphs are drawn, as shares of their own width, InDesign's glyph scaling: the width they are laid out
    /// at, and how far a justified line may narrow and widen them once its spaces and letters are at their limits.
    /// 100% throughout by default; the minimum must be above 0 (<see cref="ScalesGlyphs"/>). Upright glyphs of
    /// vertical text are not scaled.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The minimum is 0 or below.</exception>
    public SpacingRange GlyphScaling
    {
        get => _glyphScaling;
        init => _glyphScaling = ScalesGlyphs(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Glyphs scale to a positive share of their width.");
    }

    /// <summary>Whether a justified line that cannot fill its width within its spacing takes the wider glyphs its font
    /// offers for justification ('jalt', as the wide letters of Hebrew), before it scales glyphs. True by default.</summary>
    public bool JustificationAlternates { get; init; } = true;

    /// <summary>Whether a justified line of Arabic and other joined scripts stretches its words with kashidas, once
    /// its spaces are at their maximum: a stretched extender between two joined letters of each word, after a seen
    /// or a sad where the word has one, else nearest its end, up to an em each. The extender is the font's
    /// justification glyph ('JSTF'), else its tatweel (U+0640). True by default.</summary>
    public bool Kashidas { get; init; } = true;

    /// <summary>When fullwidth CJK punctuation is set half-width, its blank half trimmed, as CSS's
    /// <c>text-spacing-trim</c>: by default, punctuation side by side collapses its blanks and a closing one at the end
    /// of a line that would not fit otherwise is trimmed.</summary>
    public TextSpacingTrim SpacingTrim { get; init; }

    /// <summary>Whether an ideographic comma or full stop at the end of a line hangs past it, as Japanese burasage:
    /// by default it hangs when the line would not fit with it otherwise.</summary>
    public HangingPunctuation HangingPunctuation { get; init; } = HangingPunctuation.AllowEnd;

    /// <summary>Where the last line of a justified paragraph stands, as InDesign sets it: at its start (the default; on
    /// the right in a right-to-left paragraph), centered, at its end, or justified too
    /// (<see cref="TextRenderingParameters.JustifyLastLine"/> justifies it as well).</summary>
    public HorizontalTextAlignment LastLineAlignment { get; init; } = HorizontalTextAlignment.Left;

    /// <summary>Where a justified line holding a single word stands, as InDesign's single word justification sets it:
    /// at its start (the default), centered, at its end, or justified, its letters spread across the line.</summary>
    public HorizontalTextAlignment SingleWordJustification { get; init; } = HorizontalTextAlignment.Left;

    /// <summary>Whether <paramref name="range"/> may be a <see cref="GlyphScaling"/>: its minimum above 0.</summary>
    public static bool ScalesGlyphs(SpacingRange range) => range.Minimum > 0;
}
