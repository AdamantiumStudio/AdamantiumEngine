namespace Adamantium.Graphics.Fonts;

/// <summary>How a paragraph stands on the page. Lengths are in the units the text is laid out in; one that is no
/// number counts as none, and a line never starts past the layout's edges.</summary>
public sealed record ParagraphAttributes
{
    /// <summary>How its lines stand between its edges; null keeps the layout's alignment.</summary>
    public HorizontalTextAlignment? Alignment { get; init; }

    /// <summary>How far its lines keep from the left edge.</summary>
    public double MarginLeft { get; init; }

    /// <summary>How far its lines keep from the right edge.</summary>
    public double MarginRight { get; init; }

    /// <summary>Room above it. Between two paragraphs only the larger of this and the one above's
    /// <see cref="SpaceAfter"/> is kept; none above the first, nor at the top of a frame.</summary>
    public double SpaceBefore { get; init; }

    /// <summary>Room below it, kept as <see cref="SpaceBefore"/> is; none below the last.</summary>
    public double SpaceAfter { get; init; }

    /// <summary>How much further its first line starts than the rest, at the side its lines start from; negative hangs
    /// it out into the margin. A drop cap takes its place.</summary>
    public double TextIndent { get; init; }

    /// <summary>The height of its lines, as the layout's line height: larger text may still grow a line. NaN keeps the
    /// layout's.</summary>
    public double LineHeight { get; init; } = double.NaN;
}
