using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Text;

/// <summary>A paragraph's embedding levels by the Unicode Bidirectional Algorithm (UAX #9), by UTF-16 index: an odd
/// level runs right to left. Its lines are put in visual order by <see cref="VisualOrder"/>.</summary>
public sealed class BidiParagraph
{
    private readonly BidiResolver _resolver;
    private readonly int[] _characterOf;
    private readonly int _start;

    /// <summary>Resolves the levels of the paragraph from <paramref name="start"/> to <paramref name="end"/> of
    /// <paramref name="text"/>, its separator included.</summary>
    public BidiParagraph(string text, int start, int end, TextDirection direction)
    {
        _start = start;
        _characterOf = new int[end - start];
        var codepoints = new List<int>(end - start);
        for (var i = start; i < end; i++)
        {
            _characterOf[i - start] = codepoints.Count;
            if (char.IsHighSurrogate(text[i]) && i + 1 < end && char.IsLowSurrogate(text[i + 1]))
            {
                codepoints.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                _characterOf[i + 1 - start] = codepoints.Count - 1;
                i++;
                continue;
            }

            codepoints.Add(text[i]);
        }

        var classes = new BidiClass[codepoints.Count];
        for (var c = 0; c < classes.Length; c++)
        {
            classes[c] = BidiProperties.Class(codepoints[c]);
        }

        var level = direction switch
        {
            TextDirection.LeftToRight => 0,
            TextDirection.RightToLeft => 1,
            _ => -1
        };
        _resolver = new BidiResolver(classes, codepoints.ToArray(), level);
    }

    /// <summary>The paragraph's own level: 0 left to right, 1 right to left.</summary>
    public int BaseLevel => _resolver.ParagraphLevel;

    /// <summary>Whether text from <paramref name="start"/> to <paramref name="end"/> needs the algorithm at all: it has
    /// right-to-left or Arabic characters or explicit direction controls, or its paragraphs run right to left.</summary>
    public static bool IsNeeded(string text, int start, int end, TextDirection direction)
    {
        if (direction == TextDirection.RightToLeft)
        {
            return true;
        }

        for (var i = start; i < end; i++)
        {
            var c = text[i];
            if (c < 0x0590)
            {
                continue;
            }

            var codepoint = char.IsHighSurrogate(c) && i + 1 < end && char.IsLowSurrogate(text[i + 1])
                ? char.ConvertToUtf32(c, text[i + 1])
                : c;
            if (BidiProperties.Class(codepoint) is BidiClass.R or BidiClass.AL or BidiClass.AN or BidiClass.RLE
                or BidiClass.RLO or BidiClass.LRE or BidiClass.LRO or BidiClass.RLI or BidiClass.LRI or BidiClass.FSI)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Which way a paragraph of <paramref name="text"/> runs when its text decides (P2, P3), as text laid out
    /// <see cref="TextDirection.Auto"/> runs: right to left when its first strong character outside isolates is
    /// Hebrew or Arabic, left to right otherwise. The paragraph ends at its first separator.</summary>
    public static TextDirection DirectionOf(ReadOnlySpan<char> text)
    {
        var isolates = 0;
        for (var i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            switch (BidiProperties.Class(codepoint))
            {
                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    isolates++;
                    break;
                case BidiClass.PDI when isolates > 0:
                    isolates--;
                    break;
                case BidiClass.B:
                case BidiClass.L when isolates == 0:
                    return TextDirection.LeftToRight;
                case BidiClass.R or BidiClass.AL when isolates == 0:
                    return TextDirection.RightToLeft;
            }
        }

        return TextDirection.LeftToRight;
    }

    /// <summary>Whether the character at <paramref name="index"/> separates paragraphs: a line feed, a carriage
    /// return, a paragraph separator.</summary>
    public static bool IsParagraphSeparator(string text, int index) => BidiProperties.Class(text[index]) == BidiClass.B;

    /// <summary>The resolved level of the character at UTF-16 <paramref name="index"/> of the text.</summary>
    public int LevelAt(int index) => _resolver.Levels[_characterOf[index - _start]];

    /// <summary>The levels of the characters from <paramref name="start"/> to <paramref name="end"/> as one line shows
    /// them: separators, and white space before them and at the line's end, at the paragraph's level.</summary>
    public byte[] LineLevels(int start, int end)
    {
        var first = _characterOf[start - _start];
        var last = end - _start < _characterOf.Length ? _characterOf[end - _start] : _characterOf[_characterOf.Length - 1] + 1;
        var levels = _resolver.LineLevels(first, last);
        var byIndex = new byte[end - start];
        for (var i = start; i < end; i++)
        {
            byIndex[i - start] = levels[_characterOf[i - _start] - first];
        }

        return byIndex;
    }

    /// <summary>The positions of a line's pieces in visual order, left to right, from their levels: each run of a
    /// higher level reversed, from the highest down to the lowest odd one (L2).</summary>
    public static int[] VisualOrder(IReadOnlyList<byte> levels) => BidiResolver.Reorder(levels);
}
