using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Fonts.Shaping;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>How a stretch of text looks. A field left unset takes the value of the text's defaults.</summary>
public sealed class TextAttributes
{
    public static readonly TextAttributes Empty = new();

    /// <summary>OpenType features on top of the script's defaults.</summary>
    public IReadOnlyList<FontFeature> Features { get; init; }

    /// <summary>BCP 47 language tag, such as <c>ru</c>, <c>sr</c> or <c>zh-Hant</c>: picks the font's local forms.</summary>
    public string Language { get; init; }

    /// <summary>The font of the text, such as a bold or italic face of the family; unset takes the layout's.</summary>
    public IFont Font { get; init; }

    /// <summary>The size of the text; unset takes the size the text is laid out at. A line is as tall as its largest
    /// text.</summary>
    public double? FontSize { get; init; }

    /// <summary>What the text is drawn with that its <see cref="Font"/> lacks: thicker for a bold, slanted for an italic
    /// (<see cref="FontSynthesisRules"/>). A thickened glyph advances further.</summary>
    public FontSynthesis? Synthesis { get; init; }

    /// <summary>Space added after each character, in thousandths of an em, as InDesign's tracking; negative draws the
    /// letters closer. Unset takes the layout's <see cref="TextLayout.Tracking"/>.</summary>
    public double? Tracking { get; init; }

    /// <summary>Where the space between two letters comes from: the font's pairs, the letters' outlines or neither.
    /// Unset takes the layout's <see cref="TextLayout.Kerning"/>.</summary>
    public TextKerning? Kerning { get; init; }

    /// <summary>How far the text is raised above its line's baseline, in the layout's units, as InDesign's baseline
    /// shift; negative lowers it. Its size and the line's height stay as they are; its lines follow it.</summary>
    public double? BaselineShift { get; init; }

    /// <summary>Where the text stands across its line: on the baseline (unset), at the top, middle or bottom of the
    /// line, at the ascender or descender of the layout's font, or where the font sets a superscript or a subscript
    /// (its 'OS/2' offsets). Its size and the line's height stay as they are; a <see cref="BaselineShift"/> adds to
    /// it. In vertical text only the superscript and the subscript move it.</summary>
    public BaselineAlignment? BaselineAlignment { get; init; }

    /// <summary>The size of an object set into the text in place of a U+FFFC OBJECT REPLACEMENT CHARACTER - a control in
    /// a line of a UI's text: it advances the line by its width and stands on the baseline, its height raising the line
    /// as a tall glyph would. Nothing is drawn for it; whoever placed it draws the object at
    /// <see cref="TextLayout.GetInlineObjects"/>. Horizontal text only.</summary>
    public Size<double>? ObjectSize { get; init; }

    /// <summary>The font's palette color glyphs are drawn in ('CPAL', <see cref="IFont.ColorPalettes"/>); unset, or one
    /// the font lacks, draws them in its first.</summary>
    public int? ColorPalette { get; init; }

    public Color? Foreground { get; init; }

    /// <summary>Fill behind the text, as high as the line.</summary>
    public Color? Background { get; init; }

    public TextDecorations? Decorations { get; init; }

    /// <summary>The color of the lines; unset draws them in the text's color.</summary>
    public Color? DecorationColor { get; init; }

    /// <summary>Lines each with its own place, thickness, offset, color and dashes, drawn besides
    /// <see cref="Decorations"/>.</summary>
    public IReadOnlyList<TextDecorationLine> DecorationLines { get; init; }

    /// <summary>A line round the glyphs; unset takes the layout's <see cref="TextLayout.GlyphOutline"/>.</summary>
    public GlyphOutline Outline { get; init; }

    /// <summary>A shadow under the glyphs; unset takes the layout's <see cref="TextLayout.GlyphShadow"/>.</summary>
    public GlyphShadow Shadow { get; init; }

    /// <summary>A glow round the glyphs; unset takes the layout's <see cref="TextLayout.GlyphGlow"/>.</summary>
    public GlyphGlow Glow { get; init; }

    /// <summary>A reflection below the glyphs; unset takes the layout's <see cref="TextLayout.GlyphReflection"/>.</summary>
    public GlyphReflection Reflection { get; init; }

    /// <summary>These attributes with every field <paramref name="over"/> sets taken from it.</summary>
    public TextAttributes With(TextAttributes over)
    {
        if (over == null || ReferenceEquals(over, Empty))
        {
            return this;
        }

        if (ReferenceEquals(this, Empty))
        {
            return over;
        }

        return new TextAttributes
        {
            Features = over.Features ?? Features,
            Language = over.Language ?? Language,
            Font = over.Font ?? Font,
            FontSize = over.FontSize ?? FontSize,
            Synthesis = over.Synthesis ?? Synthesis,
            Tracking = over.Tracking ?? Tracking,
            Kerning = over.Kerning ?? Kerning,
            BaselineShift = over.BaselineShift ?? BaselineShift,
            BaselineAlignment = over.BaselineAlignment ?? BaselineAlignment,
            ObjectSize = over.ObjectSize ?? ObjectSize,
            ColorPalette = over.ColorPalette ?? ColorPalette,
            Foreground = over.Foreground ?? Foreground,
            Background = over.Background ?? Background,
            Decorations = over.Decorations ?? Decorations,
            DecorationColor = over.DecorationColor ?? DecorationColor,
            DecorationLines = over.DecorationLines ?? DecorationLines,
            Outline = over.Outline ?? Outline,
            Shadow = over.Shadow ?? Shadow,
            Glow = over.Glow ?? Glow,
            Reflection = over.Reflection ?? Reflection,
        };
    }

    /// <summary>Whether text with these attributes and with <paramref name="other"/> shapes alike: colors, lines and
    /// sizes differ freely; a palette does not, as its color glyphs are drawn anew, and tracking, baseline shift and
    /// alignment and an object's size do not, as they move them. Kerning is the layout's to compare, as an unset one
    /// takes its <see cref="TextLayout.Kerning"/>.</summary>
    public bool ShapesLike(TextAttributes other)
    {
        return Language == other.Language && ReferenceEquals(Font, other.Font) && Synthesis == other.Synthesis
                                          && ColorPalette == other.ColorPalette && Tracking == other.Tracking
                                          && BaselineShift == other.BaselineShift
                                          && BaselineAlignment == other.BaselineAlignment
                                          && Nullable.Equals(ObjectSize, other.ObjectSize)
                                          && FeaturesEqual(Features, other.Features);
    }

    private static bool FeaturesEqual(IReadOnlyList<FontFeature> left, IReadOnlyList<FontFeature> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left ?? []).SequenceEqual(right ?? []);
    }
}
