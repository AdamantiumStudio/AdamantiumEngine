using Adamantium.Fonts.Shaping;

namespace Adamantium.Fonts.Text;

/// <summary>How many columns of a monospace font text takes, as terminals and code editors count them: a grapheme two
/// when it is East Asian Wide or Fullwidth or drawn as an emoji, one otherwise.</summary>
public static class TextColumns
{
    /// <summary>The columns <paramref name="text"/> takes.</summary>
    public static int Count(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var boundaries = TextBoundaries.Graphemes(text);
        var columns = 0;
        var start = 0;
        for (var end = 1; end <= text.Length; end++)
        {
            if (boundaries[end])
            {
                columns += OfGrapheme(text, start, end);
                start = end;
            }
        }

        return columns;
    }

    /// <summary>The columns the grapheme of <paramref name="text"/> from <paramref name="start"/> to
    /// <paramref name="end"/> takes: 2 or 1; 0 when it is empty.</summary>
    public static int OfGrapheme(string text, int start, int end)
    {
        if (start >= end)
        {
            return 0;
        }

        var first = char.IsHighSurrogate(text[start]) && start + 1 < end && char.IsLowSurrogate(text[start + 1])
            ? char.ConvertToUtf32(text[start], text[start + 1])
            : text[start];
        return UnicodeData.IsWide(first) || EmojiPresentation.IsEmoji(text, start, end) ? 2 : 1;
    }
}
