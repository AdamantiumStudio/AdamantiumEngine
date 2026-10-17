namespace Adamantium.Graphics.Fonts;

/// <summary>Where the text's box starts above its first line and ends below its last, as WinUI's: a trimmed box holds
/// only the letters a layout centers by, and what reaches past it - accents, descenders - draws outside.</summary>
public enum TextLineBounds
{
    /// <summary>Whole lines, by the font's ascent, descent and line gap.</summary>
    Full,

    /// <summary>The first line starts at the top of its capital letters.</summary>
    TrimToCapHeight,

    /// <summary>The last line ends on its baseline.</summary>
    TrimToBaseline,

    /// <summary>Both: from the top of the capitals of the first line to the baseline of the last.</summary>
    Tight,
}
