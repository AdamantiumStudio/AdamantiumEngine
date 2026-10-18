namespace Adamantium.Graphics.Fonts;

/// <summary>Whether an ideographic comma or full stop at the end of a line hangs past it, as Japanese burasage and
/// CSS's <c>hanging-punctuation</c>: it is then left out when the line is measured to fit, aligned or justified.</summary>
public enum HangingPunctuation
{
    /// <summary>Nothing hangs: a comma or full stop that does not fit goes to the next line with the word before it.</summary>
    None,

    /// <summary>A comma or full stop at the end of a line hangs when the line does not fit with it otherwise.</summary>
    AllowEnd,

    /// <summary>A comma or full stop at the end of a line always hangs.</summary>
    ForceEnd,
}
