namespace Adamantium.Graphics.Fonts;

/// <summary>Which side of its base a ruby annotation stands on.</summary>
public enum RubyPosition
{
    /// <summary>Above horizontal text, to the right of vertical text.</summary>
    Over,

    /// <summary>Below horizontal text, to the left of vertical text.</summary>
    Under,
}

/// <summary>A ruby annotation, as HTML's &lt;rt&gt;: small text centered over (or under) its base - furigana over kanji,
/// a reading, a gloss. A base narrower than the annotation is spread to its width; the line grows to hold it unless its
/// height is set.</summary>
/// <param name="Text">What the annotation says.</param>
/// <param name="Position">Which side of the base it stands on.</param>
/// <param name="FontSize">Its size; null is half the base's.</param>
public sealed record RubyAnnotation(string Text, RubyPosition Position = RubyPosition.Over, double? FontSize = null);

/// <summary>A ruby base of attributed text: where it starts and ends, and the annotation over it.</summary>
public readonly struct RubyRun
{
    public RubyRun(int start, int length, RubyAnnotation annotation)
    {
        Start = start;
        Length = length;
        Annotation = annotation;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => Start + Length;

    public RubyAnnotation Annotation { get; }
}
