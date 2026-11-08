using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Document;

/// <summary>The labels pages show instead of their numbers (ISO 32000-2, 12.4.2): the catalog's <c>/PageLabels</c>
/// number tree, walked on an explicit stack. Without one a page's label is its number from one.</summary>
public sealed class PdfPageLabels
{
    private readonly List<PdfPageLabelRange> _ranges = [];

    internal PdfPageLabels(PdfDocument document)
    {
        if (!document.IsAuthenticated || document.Resolve(document.Catalog?["PageLabels"]) is not PdfDictionary root)
        {
            return;
        }

        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { root };
        var pending = new Stack<PdfDictionary>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (document.Resolve(node["Nums"]) is PdfArray numbers)
            {
                for (var i = 0; i + 1 < numbers.Count; i += 2)
                {
                    if (document.Resolve(numbers[i]) is PdfInteger { Value: >= 0 and <= int.MaxValue } start &&
                        document.Resolve(numbers[i + 1]) is PdfDictionary label)
                    {
                        _ranges.Add(Range((int)start.Value, label, document));
                    }
                }
            }

            if (document.Resolve(node["Kids"]) is PdfArray kids)
            {
                foreach (var kid in kids)
                {
                    if (document.Resolve(kid) is PdfDictionary child && visited.Add(child))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        _ranges.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
    }

    public IReadOnlyList<PdfPageLabelRange> Ranges => _ranges;

    /// <summary>The label of the page at <paramref name="pageIndex"/>, from zero.</summary>
    public string GetLabel(int pageIndex)
    {
        var range = -1;
        for (int low = 0, high = _ranges.Count - 1; low <= high;)
        {
            var middle = (low + high) / 2;
            if (_ranges[middle].StartIndex <= pageIndex)
            {
                range = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (range < 0)
        {
            return (pageIndex + 1).ToString(CultureInfo.InvariantCulture);
        }

        var found = _ranges[range];
        var number = (long)found.FirstNumber + pageIndex - found.StartIndex;
        return found.Prefix + Format(number, found.Style);
    }

    private static PdfPageLabelRange Range(int start, PdfDictionary label, PdfDocument document)
    {
        var style = (document.Resolve(label["S"]) as PdfName)?.Value switch
        {
            "D" => PdfPageLabelStyle.Decimal,
            "R" => PdfPageLabelStyle.UpperRoman,
            "r" => PdfPageLabelStyle.LowerRoman,
            "A" => PdfPageLabelStyle.UpperLetters,
            "a" => PdfPageLabelStyle.LowerLetters,
            _ => PdfPageLabelStyle.None,
        };
        var prefix = (document.Resolve(label["P"]) as PdfString)?.ToText() ?? string.Empty;
        var first = document.Resolve(label["St"]) is PdfInteger { Value: >= 1 and <= int.MaxValue } given
            ? (int)given.Value
            : 1;
        return new PdfPageLabelRange(start, style, prefix, first);
    }

    private static string Format(long number, PdfPageLabelStyle style)
    {
        return style switch
        {
            PdfPageLabelStyle.Decimal => number.ToString(CultureInfo.InvariantCulture),
            PdfPageLabelStyle.UpperRoman => Roman(number),
            PdfPageLabelStyle.LowerRoman => Roman(number).ToLowerInvariant(),
            PdfPageLabelStyle.UpperLetters => Letters(number, 'A'),
            PdfPageLabelStyle.LowerLetters => Letters(number, 'a'),
            _ => string.Empty,
        };
    }

    private static string Roman(long number)
    {
        if (number < 1 || number > 100_000)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var text = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (number >= values[i])
            {
                text.Append(symbols[i]);
                number -= values[i];
            }
        }

        return text.ToString();
    }

    /// <summary>A to Z, then the letter twice, then three times - the way the format counts.</summary>
    private static string Letters(long number, char first)
    {
        if (number < 1 || number > 26L * 1000)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        var letter = (char)(first + (number - 1) % 26);
        return new string(letter, (int)((number - 1) / 26) + 1);
    }
}
