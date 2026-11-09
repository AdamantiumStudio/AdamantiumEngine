using System;
using System.Collections.Generic;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Content;

/// <summary>Reads a content stream (ISO 32000-2, 7.8.2) one operation at a time: operands, then the operator they go
/// with. Lenient as readers are: a malformed operand is dropped, a run of operands past <see cref="MaxOperands"/> without
/// an operator is cut short, an inline image without a clean <c>EI</c> ends where one plausibly stands.</summary>
public sealed class PdfContentReader
{
    /// <summary>The most operands kept for one operator; content that never reaches an operator would grow without end.
    /// </summary>
    public const int MaxOperands = 4096;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly PdfLexer _lexer;
    private readonly PdfObjectParser _parser;
    private readonly PdfKeywordSearch _endImage = new(PdfKeywords.EndImageBytes);

    public PdfContentReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _lexer = new PdfLexer(data);
        _parser = new PdfObjectParser(_lexer) { KeywordsEndObjects = true };
    }

    public static IEnumerable<PdfContentOperation> Read(ReadOnlyMemory<byte> data)
    {
        var reader = new PdfContentReader(data);
        for (var operation = reader.Next(); operation != null; operation = reader.Next())
        {
            yield return operation;
        }
    }

    /// <summary>The next operation, or null at the end of the stream.</summary>
    public PdfContentOperation Next()
    {
        var operands = new List<PdfObject>();
        while (true)
        {
            var token = _lexer.Peek();
            switch (token.Kind)
            {
                case PdfTokenKind.EndOfData:
                    return null;
                case PdfTokenKind.Keyword
                    when token.Text is not (PdfKeywords.True or PdfKeywords.False or PdfKeywords.Null):
                    _lexer.Position = token.End;
                    return token.Text == PdfKeywords.BeginImage
                        ? new PdfContentOperation(PdfKeywords.BeginImage, operands, ReadInlineImage())
                        : new PdfContentOperation(token.Text, operands);
                case PdfTokenKind.ArrayEnd:
                case PdfTokenKind.DictionaryEnd:
                case PdfTokenKind.ProcedureStart:
                case PdfTokenKind.ProcedureEnd:
                    _lexer.Position = token.End;
                    continue;
            }

            PdfObject operand;
            try
            {
                operand = _parser.ReadObject();
            }
            catch (PdfException)
            {
                operand = null;
            }

            if (_lexer.Position <= token.Start)
            {
                _lexer.Position = token.End;
            }

            if (operand != null && operands.Count < MaxOperands)
            {
                operands.Add(operand);
            }
        }
    }

    private PdfInlineImage ReadInlineImage()
    {
        var dictionary = new PdfDictionary();
        while (true)
        {
            var token = _lexer.Peek();
            if (token.Kind == PdfTokenKind.EndOfData)
            {
                return new PdfInlineImage(dictionary, ReadOnlyMemory<byte>.Empty);
            }

            if (token.IsKeyword(PdfKeywords.ImageData))
            {
                _lexer.Position = token.End;
                break;
            }

            _lexer.Position = token.End;
            if (token.Kind != PdfTokenKind.Name)
            {
                continue;
            }

            PdfObject value;
            try
            {
                value = _parser.ReadObject();
            }
            catch (PdfException)
            {
                continue;
            }

            var key = PdfInlineImageKeys.Key(token.Text);
            dictionary.Set(key, PdfInlineImageKeys.Value(key, value));
        }

        var span = _data.Span;
        var start = _lexer.Position;
        if (start < span.Length && PdfCharacters.IsWhiteSpace(span[start]))
        {
            start++;
        }

        var end = DeclaredEnd(dictionary, span, start) ?? FindEndImage(span, start);
        var data = _data[start..Math.Min(end, span.Length)];
        _lexer.Position = Math.Min(span.Length, end);
        var close = _lexer.Peek();
        if (close.IsKeyword(PdfKeywords.EndImage))
        {
            _lexer.Position = close.End;
        }

        return new PdfInlineImage(dictionary, data);
    }

    /// <summary>Where the data ends by <c>/Length</c>, or by the size of unfiltered samples, when <c>EI</c> follows
    /// there.</summary>
    private static int? DeclaredEnd(PdfDictionary dictionary, ReadOnlySpan<byte> span, int start)
    {
        long? length = dictionary[PdfKeys.Length] is PdfInteger declared
            ? declared.Value
            : UnfilteredLength(dictionary);
        if (length is not { } count || count < 0 || start + count > span.Length)
        {
            return null;
        }

        var end = (int)(start + count);
        var at = end;
        while (at < span.Length && PdfCharacters.IsWhiteSpace(span[at]))
        {
            at++;
        }

        var after = at + PdfKeywords.EndImage.Length;
        return span[at..].StartsWith(PdfKeywords.EndImageBytes) &&
               (after == span.Length || !PdfCharacters.IsRegular(span[after]))
            ? end
            : null;
    }

    private static long? UnfilteredLength(PdfDictionary dictionary)
    {
        if (dictionary[PdfKeys.Filter] != null || dictionary[PdfKeys.Width] is not PdfInteger width ||
            dictionary[PdfKeys.Height] is not PdfInteger height)
        {
            return null;
        }

        var imageMask = dictionary[PdfKeys.ImageMask] is PdfBoolean { Value: true };
        var bits = imageMask ? 1 : dictionary[PdfKeys.BitsPerComponent] is PdfInteger bpc ? bpc.Value : 8;
        var components = imageMask ? 1 : dictionary[PdfKeys.ColorSpace] switch
        {
            PdfName
            {
                Value: PdfColorSpaceFamilies.DeviceRGB or PdfColorSpaceFamilies.CalRGB or PdfColorSpaceFamilies.Lab
            } => 3,
            PdfName { Value: PdfColorSpaceFamilies.DeviceCMYK } => 4,
            PdfName => 1,
            PdfArray { Count: > 0 } array when array[0] is PdfName { Value: PdfColorSpaceFamilies.Indexed } => 1,
            _ => 0,
        };
        if (components == 0 || width.Value <= 0 || height.Value <= 0 || bits is < 1 or > 16)
        {
            return null;
        }

        return height.Value * ((width.Value * components * bits + 7) / 8);
    }

    /// <summary>The first <c>EI</c> with white space before it and after it, followed by text rather than binary - an
    /// image's bytes can hold "EI" too. The white space before it is not data.</summary>
    private int FindEndImage(ReadOnlySpan<byte> span, int start)
    {
        var at = start;
        while (true)
        {
            var found = _endImage.Find(span, at);
            if (found < 0)
            {
                return span.Length;
            }

            at = found + 2;
            if (found <= start || !PdfCharacters.IsWhiteSpace(span[found - 1]) ||
                (found + 2 < span.Length && !PdfCharacters.IsWhiteSpace(span[found + 2])))
            {
                continue;
            }

            if (LooksLikeContent(span[(found + 2)..]))
            {
                return found - 1;
            }
        }
    }

    private static bool LooksLikeContent(ReadOnlySpan<byte> after)
    {
        var length = Math.Min(after.Length, 32);
        for (var i = 0; i < length; i++)
        {
            var value = after[i];
            if (value >= 0x7F || (value < 0x20 && !PdfCharacters.IsWhiteSpace(value)))
            {
                return false;
            }
        }

        return true;
    }
}
