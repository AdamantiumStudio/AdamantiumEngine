namespace Adamantium.Fonts.Text;

/// <summary>A box, a glue or a penalty of a paragraph, as Knuth and Plass model one for breaking it into lines.</summary>
public readonly struct ComposerItem
{
    private ComposerItem(ComposerItemKind kind, double width, double stretch, double shrink, double cost, bool flagged,
        double after = 0, bool discretionary = false)
    {
        Kind = kind;
        Width = width;
        Stretch = stretch;
        Shrink = shrink;
        Cost = cost;
        Flagged = flagged;
        After = after;
        Discretionary = discretionary;
    }

    public ComposerItemKind Kind { get; }

    /// <summary>The width of a box or a glue; for a penalty, what a line ending at it adds, such as a hyphen.</summary>
    public double Width { get; }

    /// <summary>How much more than its width a glue may take; <see cref="double.PositiveInfinity"/> fills any line,
    /// as the glue ending a paragraph does. May be negative, to take back another glue's stretch.</summary>
    public double Stretch { get; }

    /// <summary>How much less than its width a glue may take.</summary>
    public double Shrink { get; }

    /// <summary>What ending a line at a penalty costs: <see cref="ParagraphComposer.Never"/> or more forbids it,
    /// <see cref="ParagraphComposer.Forced"/> or less forces it.</summary>
    public double Cost { get; }

    /// <summary>Whether a line ending at a penalty ends in a hyphen, which two lines in a row are kept from.</summary>
    public bool Flagged { get; }

    /// <summary>For a penalty, what the line after it adds when a line ends there: the rest of a word broken at a hyphen
    /// set again by itself may take more or less than it did whole, as TeX's discretionary break.</summary>
    public double After { get; }

    // Made with an After: a break inside a word, which adds a hyphen whatever its width comes to.
    internal bool Discretionary { get; }

    public static ComposerItem Box(double width) => new(ComposerItemKind.Box, width, 0, 0, 0, false);

    public static ComposerItem Glue(double width, double stretch, double shrink) =>
        new(ComposerItemKind.Glue, width, stretch, shrink, 0, false);

    public static ComposerItem Penalty(double width, double cost, bool flagged) =>
        new(ComposerItemKind.Penalty, width, 0, 0, cost, flagged);

    /// <summary>A penalty whose break also changes the line after it by <paramref name="after"/>: a hyphen in a word
    /// whose halves are set again by themselves.</summary>
    public static ComposerItem Penalty(double width, double cost, bool flagged, double after) =>
        new(ComposerItemKind.Penalty, width, 0, 0, cost, flagged, after, true);
}
