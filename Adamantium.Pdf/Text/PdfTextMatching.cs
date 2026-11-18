using System;
using System.Globalization;

namespace Adamantium.Pdf.Text;

/// <summary>Finds a query in plain text by the rules <see cref="PdfPageText.Find"/> uses - case, accents and whole
/// words as <see cref="PdfTextSearchOptions"/> say - so a bookmark's title or a comment is searched as a page is.</summary>
public static class PdfTextMatching
{
    /// <summary>Where <paramref name="query"/> is first in <paramref name="text"/> at or after <paramref name="from"/>;
    /// -1 for nowhere, <paramref name="length"/> how long the match is there.</summary>
    public static int IndexOf(string text, string query, PdfTextSearchOptions options, int from, out int length)
    {
        length = 0;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return -1;
        }

        options ??= PdfTextSearchOptions.Default;
        var compare = (options.MatchCase ? CompareOptions.None : CompareOptions.IgnoreCase) |
                      (options.MatchDiacritics ? CompareOptions.None : CompareOptions.IgnoreNonSpace);
        var info = CultureInfo.InvariantCulture.CompareInfo;
        while (from < text.Length)
        {
            var found = info.IndexOf(text.AsSpan(from), query, compare, out var matched);
            if (found < 0 || matched <= 0)
            {
                return -1;
            }

            var at = from + found;
            if (!options.WholeWord || (!IsWordCharacter(text, at - 1) && !IsWordCharacter(text, at + matched)))
            {
                length = matched;
                return at;
            }

            from = at + matched;
        }

        return -1;
    }

    private static bool IsWordCharacter(string text, int index) =>
        index >= 0 && index < text.Length && char.IsLetterOrDigit(text[index]);
}
