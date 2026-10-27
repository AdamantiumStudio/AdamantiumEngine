using Adamantium.Core.Fonts;

namespace Adamantium.Fonts;

/// <summary>When synthesis draws a face the family lacks, and how much: the rules browsers follow.</summary>
public static class FontSynthesisRules
{
    /// <summary>The slant of a synthesized italic: the horizontal shift per unit of height above the baseline (about
    /// 14 degrees, as Skia and the browsers slant).</summary>
    public const double Slant = 0.25;

    /// <summary>What to synthesize for text asked for at <paramref name="weight"/> and <paramref name="style"/> and set in
    /// <paramref name="font"/>, the face matching found, when <paramref name="allowed"/> permits it: the weight when bold
    /// was asked for and the face is lighter than 600, the style when italic or oblique was asked for and the face is
    /// upright.</summary>
    public static FontSynthesis Needed(IFont font, FontWeight weight, FontStyle style, FontSynthesis allowed)
    {
        var needed = FontSynthesis.None;
        if ((allowed & FontSynthesis.Weight) != 0 && weight.Value >= 600 && font.Weight.Value < 600)
        {
            needed |= FontSynthesis.Weight;
        }

        if ((allowed & FontSynthesis.Style) != 0 && style != FontStyle.Normal && font.Style == FontStyle.Normal)
        {
            needed |= FontSynthesis.Style;
        }

        return needed;
    }

    /// <summary>How far a synthesized bold thickens each side of a glyph's outline, in ems: a 24th of the size at
    /// 9 pixels and below, a 32nd at 36 and above, halved per side (as Skia emboldens). The glyph's advance grows by
    /// twice this.</summary>
    public static double EmboldenPerSide(double fontSize)
    {
        var t = fontSize <= 9 ? 0 : fontSize >= 36 ? 1 : (fontSize - 9) / 27;
        return (1.0 / 24 + (1.0 / 32 - 1.0 / 24) * t) / 2;
    }
}
