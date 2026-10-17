namespace Adamantium.Graphics.Fonts;

/// <summary>Where the space between two letters comes from, as InDesign's kerning.</summary>
public enum TextKerning
{
    /// <summary>The font's own pairs ('kern' in GPOS or the 'kern' table): the default.</summary>
    Metrics,

    /// <summary>Measured from the letters' outlines (<see cref="Adamantium.Fonts.Shaping.OpticalKerning"/>): for a font
    /// with no pairs, or pairs the font left out, such as a letter beside a figure. Horizontal left-to-right text of
    /// the scripts whose letters stand apart (<see cref="Adamantium.Fonts.Shaping.OpticalKerning.Applies"/>); right to
    /// left, vertical text and other scripts keep the font's pairs, and two letters of different sizes are left as they
    /// are. The font's 'kern' is off where it applies, whatever the text's features ask.</summary>
    Optical,

    /// <summary>No kerning: each letter takes its own advance; the font's 'kern' is off, whatever the text's features
    /// ask.</summary>
    None,
}
