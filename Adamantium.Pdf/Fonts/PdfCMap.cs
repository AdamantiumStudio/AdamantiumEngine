using System;
using System.Collections.Generic;

namespace Adamantium.Pdf.Fonts;

/// <summary>A CMap (ISO 32000-2, 9.7.5 and 9.10.3): how a string splits into character codes, and the CID or the
/// text each code stands for.</summary>
public sealed class PdfCMap
{
    /// <summary>The most codespace ranges a CMap keeps; Adobe's own CMaps hold a few dozen.</summary>
    public const int MaxCodeSpaceRanges = 256;

    private readonly List<PdfCodeSpaceRange> _codeSpaces = [];
    private readonly List<PdfCodeRange> _cids = [];
    private readonly List<PdfCodeRange> _texts = [];
    private List<PdfCodeSpaceRange>[,] _codeSpacesByFirstByte;

    internal PdfCMap(string name)
    {
        Name = name;
    }

    /// <summary>Identity-H or Identity-V: two-byte codes, each its own CID.</summary>
    public static PdfCMap Identity(bool vertical)
    {
        var map = new PdfCMap(vertical ? PdfEncodingNames.IdentityV : PdfEncodingNames.IdentityH)
        {
            IsVertical = vertical,
        };
        map._codeSpaces.Add(new PdfCodeSpaceRange(2, 0, 0xFFFF));
        map._cids.Add(new PdfCodeRange(2, 0, 0xFFFF, 0));
        map.Seal();
        return map;
    }

    public string Name { get; internal set; }

    /// <summary>Whether text in a font with this CMap is written top to bottom (<c>/WMode 1</c>).</summary>
    public bool IsVertical { get; internal set; }

    /// <summary>The code at <paramref name="position"/> of a string, by the codespace ranges: the first length whose
    /// range holds it; a code no range holds takes the length of a range its first byte starts, or the shortest.
    /// </summary>
    /// <returns>The code's length in bytes, at least 1 while bytes remain.</returns>
    public int ReadCode(ReadOnlySpan<byte> text, int position, out uint code)
    {
        code = 0;
        if (position >= text.Length)
        {
            return 0;
        }

        if (_codeSpaces.Count == 0)
        {
            code = text[position];
            return 1;
        }

        var first = text[position];
        var shortest = int.MaxValue;
        var startedBy = int.MaxValue;
        uint value = 0;
        for (var length = 1; length <= 4; length++)
        {
            var candidates = _codeSpacesByFirstByte[length - 1, first];
            if (position + length <= text.Length)
            {
                value = (value << 8) | text[position + length - 1];
            }

            if (candidates == null)
            {
                continue;
            }

            startedBy = Math.Min(startedBy, length);
            if (position + length > text.Length)
            {
                continue;
            }

            foreach (var range in candidates)
            {
                if (range.Contains(value, length))
                {
                    code = value;
                    return length;
                }
            }
        }

        foreach (var range in _codeSpaces)
        {
            shortest = Math.Min(shortest, range.Length);
        }

        var fallback = Math.Min(startedBy != int.MaxValue ? startedBy : shortest, text.Length - position);
        for (var i = 0; i < fallback; i++)
        {
            code = (code << 8) | text[position + i];
        }

        return fallback;
    }

    /// <summary>The CID of a code; 0, the missing glyph, when the CMap maps it to none.</summary>
    public uint CidOf(uint code, int length)
    {
        var range = Find(_cids, code, length);
        return range is { } found ? found.Start + (code - found.Origin) : 0;
    }

    /// <summary>The text a code stands for in a ToUnicode CMap, or null.</summary>
    public string TextOf(uint code, int length)
    {
        if (Find(_texts, code, length) is not { } range)
        {
            return null;
        }

        if (range.Texts != null)
        {
            var index = code - range.Origin;
            return index < range.Texts.Length ? range.Texts[index] : null;
        }

        if (string.IsNullOrEmpty(range.Text))
        {
            return range.Text;
        }

        var offset = (int)(code - range.Origin);
        if (offset == 0)
        {
            return range.Text;
        }

        var last = range.Text[^1] + offset;
        return last > char.MaxValue ? null : range.Text[..^1] + (char)last;
    }

    internal void AddCodeSpace(PdfCodeSpaceRange range)
    {
        if (_codeSpaces.Count < MaxCodeSpaceRanges)
        {
            _codeSpaces.Add(range);
        }
    }

    internal void AddCids(PdfCodeRange range) => _cids.Add(range);

    internal void AddTexts(PdfCodeRange range) => _texts.Add(range);

    /// <summary>Takes what <paramref name="parent"/> defines and this CMap does not (<c>usecmap</c>).</summary>
    internal void Use(PdfCMap parent)
    {
        if (_codeSpaces.Count == 0)
        {
            _codeSpaces.AddRange(parent._codeSpaces);
        }

        _cids.InsertRange(0, parent._cids);
        _texts.InsertRange(0, parent._texts);
        IsVertical |= parent.IsVertical;
    }

    /// <summary>Readies the CMap for lookup: codespace ranges indexed by length and first byte, and mappings cut into
    /// pieces that do not overlap, a range given later - by the file, or by a CMap over the one it uses - covering
    /// what earlier ones gave the same codes.</summary>
    internal void Seal()
    {
        _codeSpacesByFirstByte = new List<PdfCodeSpaceRange>[4, 256];
        foreach (var range in _codeSpaces)
        {
            if (range.Length is < 1 or > 4)
            {
                continue;
            }

            for (var first = 0; first < 256; first++)
            {
                if (range.StartsWith((byte)first))
                {
                    (_codeSpacesByFirstByte[range.Length - 1, first] ??= []).Add(range);
                }
            }
        }

        Flatten(_cids);
        Flatten(_texts);
    }

    private static void Flatten(List<PdfCodeRange> ranges)
    {
        var pieces = new List<PdfCodeRange>(ranges.Count);
        for (var length = 1; length <= 4; length++)
        {
            var ofLength = new List<PdfCodeRange>();
            var bounds = new List<(long Low, long High)>();
            foreach (var range in ranges)
            {
                if (range.Length == length)
                {
                    ofLength.Add(range);
                    bounds.Add((range.Low, range.High));
                }
            }

            foreach (var (low, high, index) in PdfRangeFlattener.Flatten(bounds))
            {
                pieces.Add(ofLength[index].Piece((uint)low, (uint)high));
            }
        }

        ranges.Clear();
        ranges.AddRange(pieces);
    }

    private static PdfCodeRange? Find(List<PdfCodeRange> ranges, uint code, int length)
    {
        int low = 0, high = ranges.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var range = ranges[middle];
            var compare = range.Length != length ? range.Length.CompareTo(length) : range.Low.CompareTo(code);
            if (compare <= 0)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 && ranges[found].Contains(code, length) ? ranges[found] : null;
    }
}
