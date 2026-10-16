namespace Adamantium.Graphics.Fonts;

/// <summary>A paragraph of attributed text: where it starts and ends, separator included, and how it stands.</summary>
public readonly struct ParagraphRun
{
    public ParagraphRun(int start, int length, ParagraphAttributes attributes)
    {
        Start = start;
        Length = length;
        Attributes = attributes;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => Start + Length;

    public ParagraphAttributes Attributes { get; }
}
