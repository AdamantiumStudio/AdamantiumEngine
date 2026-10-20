using System;
using Adamantium.Fonts;

namespace Adamantium.Graphics.Fonts;

/// <summary>The first characters of a text set large across its first lines, which run beside them: its top at the
/// cap height of the first line, its foot on the baseline of the last.</summary>
public sealed class DropCap : IEquatable<DropCap>
{
    /// <summary>The fewest lines a drop cap spans.</summary>
    public const int LeastLines = 2;

    public DropCap(int lines, int characters = 1, IFont font = null)
    {
        if (lines < LeastLines)
        {
            throw new ArgumentOutOfRangeException(nameof(lines), lines, "A drop cap spans two lines or more.");
        }

        if (characters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(characters), characters, "A drop cap has a character or more.");
        }

        Lines = lines;
        Characters = characters;
        Font = font;
    }

    /// <summary>How many lines the drop cap spans.</summary>
    public int Lines { get; }

    /// <summary>How many characters - graphemes - of the text it takes.</summary>
    public int Characters { get; }

    /// <summary>The font it is set in, as a decorative face of initials; null sets it in the text's own.</summary>
    public IFont Font { get; }

    public bool Equals(DropCap other) =>
        other != null && Lines == other.Lines && Characters == other.Characters && ReferenceEquals(Font, other.Font);

    public override bool Equals(object obj) => obj is DropCap other && Equals(other);

    public override int GetHashCode() => (Lines * 397) ^ Characters;
}
