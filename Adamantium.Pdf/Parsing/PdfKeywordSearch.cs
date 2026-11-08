using System;
using System.Collections.Generic;

namespace Adamantium.Pdf.Parsing;

/// <summary>Finds occurrences of one keyword in one piece of data. Every occurrence is collected on the first search,
/// in one pass, and each search after is a binary search - in any order of starts.</summary>
internal sealed class PdfKeywordSearch
{
    private readonly byte[] _keyword;
    private readonly bool _wholeWord;
    private List<int> _found;

    public PdfKeywordSearch(ReadOnlySpan<byte> keyword, bool wholeWord = false)
    {
        _keyword = keyword.ToArray();
        _wholeWord = wholeWord;
    }

    /// <summary>Where the keyword next starts at or after <paramref name="start"/>, or -1.</summary>
    public int Find(ReadOnlySpan<byte> data, int start)
    {
        _found ??= Collect(data);
        var index = _found.BinarySearch(start);
        if (index < 0)
        {
            index = ~index;
        }

        return index < _found.Count ? _found[index] : -1;
    }

    private List<int> Collect(ReadOnlySpan<byte> data)
    {
        var found = new List<int>();
        var position = 0;
        while (position < data.Length)
        {
            var at = data[position..].IndexOf(_keyword);
            if (at < 0)
            {
                break;
            }

            at += position;
            position = at + 1;
            if (_wholeWord && ((at > 0 && PdfCharacters.IsRegular(data[at - 1])) ||
                               (at + _keyword.Length < data.Length &&
                                PdfCharacters.IsRegular(data[at + _keyword.Length]))))
            {
                continue;
            }

            found.Add(at);
        }

        return found;
    }
}
