using System;
using System.Collections.Generic;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.CrossReference;

/// <summary>Reads the file's cross-reference from <c>startxref</c> back through <c>/Prev</c> (ISO 32000-2, 7.5.4,
/// 7.5.8): tables, streams and hybrid files with <c>/XRefStm</c>. Throws when it cannot, so the file is repaired.
/// </summary>
internal sealed class PdfCrossReferenceReader
{
    private static readonly HashSet<string> SectionKeys =
    [
        "Prev", "XRefStm", "Type", "W", "Index", "Length", "Filter", "DecodeParms", "DL",
    ];

    private readonly ReadOnlyMemory<byte> _data;
    private readonly PdfLexer _lexer;
    private readonly PdfObjectParser _parser;
    private readonly PdfDecodeLimits _limits;

    public PdfCrossReferenceReader(ReadOnlyMemory<byte> data, PdfDecodeLimits limits)
    {
        _data = data;
        _lexer = new PdfLexer(data);
        _parser = new PdfObjectParser(_lexer);
        _limits = limits;
    }

    /// <summary>Where <c>%PDF-</c> starts: offsets of a file with bytes before its header may count from there.
    /// </summary>
    public int HeaderOffset { get; private set; }

    public PdfCrossReference Read()
    {
        var span = _data.Span;
        HeaderOffset = Math.Max(0, span[..Math.Min(span.Length, 1024)].IndexOf("%PDF-"u8));
        var startXref = span.LastIndexOf("startxref"u8);
        if (startXref < 0)
        {
            throw new PdfException("The file has no startxref.");
        }

        _lexer.Position = startXref + "startxref".Length;
        var offset = _lexer.Next();
        if (offset.Kind != PdfTokenKind.Integer)
        {
            throw new PdfException("startxref is not followed by an offset.");
        }

        var result = new PdfCrossReference(new PdfDictionary());
        var visited = new HashSet<long>();
        long next = offset.Integer;
        while (next >= 0 && visited.Add(next))
        {
            var trailer = ReadSectionAt(next, result);
            if (trailer["XRefStm"] is PdfInteger hidden && visited.Add(hidden.Value))
            {
                ReadSectionAt(hidden.Value, result);
            }

            MergeTrailer(result, trailer);
            next = trailer["Prev"] is PdfInteger previous ? previous.Value : -1;
        }

        if (result.Trailer["Root"] == null)
        {
            throw new PdfException("The trailer has no /Root.");
        }

        return result;
    }

    private static void MergeTrailer(PdfCrossReference result, PdfDictionary trailer)
    {
        foreach (var (key, value) in trailer)
        {
            if (!SectionKeys.Contains(key) && !result.Trailer.ContainsKey(key))
            {
                result.Trailer.Set(key, value);
            }
        }
    }

    private PdfDictionary ReadSectionAt(long offset, PdfCrossReference result)
    {
        return TryReadSection(offset, result)
               ?? TryReadSection(offset + HeaderOffset, result)
               ?? throw new PdfException("No cross-reference section at offset " + offset + ".");
    }

    private PdfDictionary TryReadSection(long offset, PdfCrossReference result)
    {
        if (offset < 0 || offset >= _data.Length)
        {
            return null;
        }

        _lexer.Position = (int)offset;
        var first = _lexer.Peek();
        if (first.IsKeyword("xref"))
        {
            _lexer.Position = first.End;
            return ReadTable(result);
        }

        if (first.Kind == PdfTokenKind.Integer)
        {
            var indirect = _parser.ReadIndirectObject((int)offset);
            if (indirect?.Value is PdfStream stream && stream.Dictionary["Type"] is PdfName { Value: "XRef" })
            {
                ReadStream(stream, result);
                return stream.Dictionary;
            }
        }

        return null;
    }

    private PdfDictionary ReadTable(PdfCrossReference result)
    {
        while (true)
        {
            var token = _lexer.Next();
            if (token.IsKeyword("trailer"))
            {
                return _parser.ReadObject() as PdfDictionary
                       ?? throw new PdfException("The trailer is not a dictionary.");
            }

            var count = _lexer.Next();
            if (token.Kind != PdfTokenKind.Integer || count.Kind != PdfTokenKind.Integer || token.Integer < 0 ||
                count.Integer < 0 || token.Integer + count.Integer > int.MaxValue)
            {
                throw new PdfException("A cross-reference table is malformed.");
            }

            for (var i = 0; i < count.Integer; i++)
            {
                var offset = _lexer.Next();
                var generation = _lexer.Next();
                var kind = _lexer.Next();
                if (offset.Kind != PdfTokenKind.Integer || generation.Kind != PdfTokenKind.Integer ||
                    kind.Kind != PdfTokenKind.Keyword)
                {
                    throw new PdfException("A cross-reference table entry is malformed.");
                }

                var number = (int)(token.Integer + i);
                var entry = kind.Text == "n"
                    ? new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, offset.Integer,
                        (int)Math.Clamp(generation.Integer, 0, ushort.MaxValue))
                    : PdfCrossReferenceEntry.Free;
                result.AddOlder(number, entry);
            }
        }
    }

    private void ReadStream(PdfStream stream, PdfCrossReference result)
    {
        var dictionary = stream.Dictionary;
        if (dictionary["W"] is not PdfArray { Count: 3 } widths)
        {
            throw new PdfException("A cross-reference stream has no /W.");
        }

        Span<int> w = stackalloc int[3];
        for (var i = 0; i < 3; i++)
        {
            w[i] = widths[i] is PdfInteger width && width.Value is >= 0 and <= 8 ? (int)width.Value : -1;
            if (w[i] < 0)
            {
                throw new PdfException("A cross-reference stream's /W is malformed.");
            }
        }

        var rowLength = w[0] + w[1] + w[2];
        if (rowLength == 0)
        {
            return;
        }

        var data = PdfStreamDecoder.Decode(dictionary, stream.Data, PdfDirectResolver.Instance, _limits).Data;
        var ranges = new List<(long Start, long Count)>();
        if (dictionary["Index"] is PdfArray index)
        {
            for (var i = 0; i + 1 < index.Count; i += 2)
            {
                if (index[i] is PdfInteger start && index[i + 1] is PdfInteger count)
                {
                    ranges.Add((start.Value, count.Value));
                }
            }
        }
        else
        {
            ranges.Add((0, dictionary["Size"] is PdfInteger size ? size.Value : data.Length / rowLength));
        }

        var row = 0;
        foreach (var (start, count) in ranges)
        {
            for (long i = 0; i < count && (row + 1L) * rowLength <= data.Length; i++, row++)
            {
                var number = start + i;
                if (number < 0 || number > int.MaxValue)
                {
                    continue;
                }

                var at = row * rowLength;
                var type = w[0] == 0 ? 1 : Field(data, at, w[0]);
                var second = Field(data, at + w[0], w[1]);
                var third = Field(data, at + w[0] + w[1], w[2]);
                var entry = type switch
                {
                    1 => new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, second,
                        (int)Math.Clamp(third, 0, ushort.MaxValue)),
                    2 => new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InObjectStream, 0, 0,
                        (int)Math.Clamp(second, 0, int.MaxValue), (int)Math.Clamp(third, 0, int.MaxValue)),
                    _ => PdfCrossReferenceEntry.Free,
                };
                result.AddOlder((int)number, entry);
            }
        }
    }

    private static long Field(ReadOnlyMemory<byte> data, int at, int width)
    {
        return Field(data.Span, at, width);
    }

    private static long Field(ReadOnlySpan<byte> data, int at, int width)
    {
        long value = 0;
        for (var i = 0; i < width; i++)
        {
            value = (value << 8) | data[at + i];
        }

        return value;
    }
}
