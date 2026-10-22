using System;
using System.Globalization;
using Adamantium.Fonts;
using Adamantium.Fonts.Text;
using Adamantium.Graphics.Fonts.Layout;

namespace Adamantium.Graphics.Fonts;

/// <summary>The first characters of a text set large across its first lines, which run beside them: sized by its
/// letter's capital, or a letter standing for its script (ה, alef, 国, 한), or the letter itself where a font lacks
/// that one - an accented letter by its letter - whose top stands as high as in the first line; its foot on the
/// baseline of the last. A lowercase letter stands lower, an accent higher, as in the text.</summary>
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

    /// <summary>Whether a drop cap can start <paramref name="text"/>: not one that is empty or starts with a blank, a
    /// control character or an object.</summary>
    public static bool CanStart(ReadOnlySpan<char> text) =>
        !text.IsEmpty && !char.IsWhiteSpace(text[0]) && !char.IsControl(text[0]) &&
        text[0] != TextGeometry.ObjectReplacement;

    /// <summary>How many characters of <paramref name="text"/> it takes from the start, an opening quote or bracket
    /// with the letter after it; 0 for a text it cannot start (<see cref="CanStart"/>).</summary>
    public int Length(string text)
    {
        if (!CanStart(text))
        {
            return 0;
        }

        var graphemes = TextBoundaries.Graphemes(text);
        var end = 0;
        var letters = 0;
        while (end < text.Length && letters < Characters &&
               text[end] is not ('\n' or TextGeometry.LineSeparator or TextGeometry.ParagraphSeparator
                   or TextGeometry.ObjectReplacement))
        {
            if (Counts(text, end))
            {
                letters++;
            }

            end = TextBoundaries.Next(graphemes, end);
        }

        return end;
    }

    /// <summary>How many <see cref="Characters"/> a drop cap of <paramref name="letters"/> has: its graphemes, an
    /// opening quote or bracket not counted.</summary>
    public static int CharactersOf(string letters)
    {
        if (string.IsNullOrEmpty(letters))
        {
            return 0;
        }

        var graphemes = TextBoundaries.Graphemes(letters);
        var count = 0;
        for (var at = 0; at < letters.Length; at = TextBoundaries.Next(graphemes, at))
        {
            if (Counts(letters, at))
            {
                count++;
            }
        }

        return count;
    }

    private static bool Counts(string text, int at) =>
        CharUnicodeInfo.GetUnicodeCategory(text, at) is not (UnicodeCategory.OpenPunctuation
            or UnicodeCategory.InitialQuotePunctuation);

    public bool Equals(DropCap other) =>
        other != null && Lines == other.Lines && Characters == other.Characters && ReferenceEquals(Font, other.Font);

    public override bool Equals(object obj) => obj is DropCap other && Equals(other);

    public override int GetHashCode() => (Lines * 397) ^ Characters;
}
