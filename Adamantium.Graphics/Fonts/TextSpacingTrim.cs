namespace Adamantium.Graphics.Fonts;

/// <summary>When fullwidth CJK punctuation - brackets, quotes, the ideographic comma and full stop - is set half-width,
/// its blank half trimmed, as CSS's <c>text-spacing-trim</c>.</summary>
public enum TextSpacingTrim
{
    /// <summary>Opening punctuation full-width at the start of each line, closing punctuation half-width at the end of a
    /// line only when the line does not fit otherwise, and the blanks of punctuation side by side collapsed.</summary>
    Normal,

    /// <summary>Every fullwidth punctuation full-width.</summary>
    SpaceAll,

    /// <summary>As <see cref="Normal"/>, but opening punctuation half-width at the start of a line other than the first
    /// of a paragraph.</summary>
    SpaceFirst,

    /// <summary>As <see cref="Normal"/>, but opening punctuation half-width at the start of every line.</summary>
    TrimStart,

    /// <summary>Opening punctuation half-width at the start of every line, closing punctuation at the end of every
    /// line, and the blanks of punctuation side by side collapsed.</summary>
    TrimBoth,

    /// <summary>Every fullwidth opening, closing and middle dot punctuation half-width, wherever it stands.</summary>
    TrimAll,
}
