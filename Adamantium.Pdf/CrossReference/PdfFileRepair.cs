using System;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.CrossReference;

/// <summary>Builds a cross-reference for a file whose own cannot be read: every <c>N G obj</c> in the file, the last
/// definition of a number winning, and the trailer from the last <c>trailer</c> dictionaries that name a
/// <c>/Root</c>. Each piece is read only up to where the next begins, so the whole stays linear however broken the
/// pieces are.</summary>
internal static class PdfFileRepair
{
    public static PdfCrossReference Scan(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var result = new PdfCrossReference(new PdfDictionary()) { IsRepaired = true };
        var objects = new PdfKeywordSearch(PdfKeywords.ObjBytes);
        var streams = new PdfKeywordSearch(PdfKeywords.StreamBytes, true);
        var position = 0;
        while (position < span.Length)
        {
            var at = objects.Find(span, position);
            if (at < 0)
            {
                break;
            }

            var afterKeyword = at + PdfKeywords.Obj.Length;
            position = afterKeyword;
            if (!EndsWord(span, afterKeyword) ||
                !TryHeader(span, at, out var start, out var number, out var generation))
            {
                continue;
            }

            result.Set(number, new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, start, generation));
            position = Math.Max(position, ProvenStreamEnd(data, afterKeyword, objects, streams));
        }

        ReadTrailers(data, result);
        return result;
    }

    /// <summary>Where the stream of the object whose header ends at <paramref name="afterHeader"/> ends - when its
    /// <c>/Length</c> is a number that <c>endstream</c> confirms. Only such a stream's bytes are skipped, so that
    /// <c>obj</c> inside them is not taken for an object while a broken one hides nothing.</summary>
    private static int ProvenStreamEnd(ReadOnlyMemory<byte> data, int afterHeader, PdfKeywordSearch objects,
        PdfKeywordSearch streams)
    {
        var span = data.Span;
        var stream = streams.Find(span, afterHeader);
        var nextObject = objects.Find(span, afterHeader);
        if (stream < 0 || (nextObject >= 0 && nextObject < stream))
        {
            return -1;
        }

        PdfObject dictionary;
        try
        {
            dictionary = new PdfObjectParser(new PdfLexer(data[afterHeader..stream])).ReadObject();
        }
        catch (PdfException)
        {
            return -1;
        }

        if ((dictionary as PdfDictionary)?[PdfKeys.Length] is not PdfInteger { Value: >= 0 } length)
        {
            return -1;
        }

        var dataStart = stream + PdfKeywords.Stream.Length;
        if (dataStart < span.Length && span[dataStart] == '\r')
        {
            dataStart++;
        }

        if (dataStart < span.Length && span[dataStart] == '\n')
        {
            dataStart++;
        }

        var end = dataStart + length.Value;
        return end <= span.Length && PdfObjectParser.EndStreamFollows(span, (int)end) ? (int)end : -1;
    }

    private static void ReadTrailers(ReadOnlyMemory<byte> data, PdfCrossReference result)
    {
        var span = data.Span;
        var trailers = new PdfKeywordSearch(PdfKeywords.TrailerBytes, true);
        var at = trailers.Find(span, 0);
        while (at >= 0)
        {
            var start = at + PdfKeywords.Trailer.Length;
            var next = trailers.Find(span, start);
            var end = next < 0 ? span.Length : next;
            at = next;
            PdfObject trailer;
            try
            {
                trailer = new PdfObjectParser(new PdfLexer(data[start..end])).ReadObject();
            }
            catch (PdfException)
            {
                continue;
            }

            if (trailer is PdfDictionary dictionary && dictionary[PdfKeys.Root] != null)
            {
                foreach (var (key, value) in dictionary)
                {
                    if (key is not (PdfKeys.Prev or PdfKeys.XRefStm))
                    {
                        result.Trailer.Set(key, value);
                    }
                }
            }
        }
    }

    /// <summary>Whether <c>obj</c> at <paramref name="at"/> is preceded by <c>N G</c> - digits, white space, digits,
    /// optional white space - that start a word.</summary>
    private static bool TryHeader(ReadOnlySpan<byte> span, int at, out int start, out int number, out int generation)
    {
        start = 0;
        number = 0;
        generation = 0;
        var position = at - 1;
        while (position >= 0 && PdfCharacters.IsWhiteSpace(span[position]))
        {
            position--;
        }

        var generationEnd = position + 1;
        while (position >= 0 && PdfCharacters.IsDigit(span[position]) && generationEnd - position <= 5)
        {
            position--;
        }

        var generationStart = position + 1;
        if (generationStart == generationEnd || position < 0 || !PdfCharacters.IsWhiteSpace(span[position]))
        {
            return false;
        }

        while (position >= 0 && PdfCharacters.IsWhiteSpace(span[position]))
        {
            position--;
        }

        var numberEnd = position + 1;
        while (position >= 0 && PdfCharacters.IsDigit(span[position]) && numberEnd - position <= 10)
        {
            position--;
        }

        var numberStart = position + 1;
        if (numberStart == numberEnd || (position >= 0 && PdfCharacters.IsRegular(span[position])))
        {
            return false;
        }

        if (!TryInteger(span[numberStart..numberEnd], out number) ||
            !TryInteger(span[generationStart..generationEnd], out generation) || generation > ushort.MaxValue)
        {
            return false;
        }

        start = numberStart;
        return true;
    }

    private static bool EndsWord(ReadOnlySpan<byte> span, int position) =>
        position >= span.Length || !PdfCharacters.IsRegular(span[position]);

    private static bool TryInteger(ReadOnlySpan<byte> digits, out int value)
    {
        long result = 0;
        foreach (var digit in digits)
        {
            result = result * 10 + (digit - '0');
        }

        value = (int)Math.Min(result, int.MaxValue);
        return result <= int.MaxValue;
    }
}
