using System;
using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Parsing;

/// <summary>Reads objects out of tokens (ISO 32000-2, 7.3). Arrays and dictionaries nest on an explicit stack, so a
/// hostile depth cannot overflow the call stack; past <see cref="MaxDepth"/> the object is refused.</summary>
internal sealed class PdfObjectParser
{
    /// <summary>The deepest nesting of arrays and dictionaries read.</summary>
    public const int MaxDepth = 512;

    private readonly PdfLexer _lexer;
    private readonly IPdfLengthResolver _lengths;
    private readonly Stack<PdfParserFrame> _stack = new();
    private readonly PdfKeywordSearch _endStream = new(PdfKeywords.EndStreamBytes);
    private readonly PdfKeywordSearch _endObj = new(PdfKeywords.EndObjBytes);

    public PdfObjectParser(PdfLexer lexer, IPdfLengthResolver lengths = null)
    {
        _lexer = lexer;
        _lengths = lengths;
    }

    public PdfLexer Lexer => _lexer;

    /// <summary>Whether any keyword that is not a value closes what is open, as in a content stream, where an operator
    /// inside an array means the array was never closed.</summary>
    public bool KeywordsEndObjects { get; init; }

    /// <summary>Reads the object at the lexer's position, or returns null at the end of the data or at a keyword that
    /// is not a value (<c>endobj</c>, <c>stream</c> and the like), which is left unread.</summary>
    public PdfObject ReadObject()
    {
        _stack.Clear();
        while (true)
        {
            var token = _lexer.Next();
            PdfObject value;
            switch (token.Kind)
            {
                case PdfTokenKind.EndOfData:
                    return Unwind();
                case PdfTokenKind.ArrayStart:
                    Push(new PdfParserFrame(new PdfArray(), null));
                    continue;
                case PdfTokenKind.DictionaryStart:
                    Push(new PdfParserFrame(null, new PdfDictionary()));
                    continue;
                case PdfTokenKind.ArrayEnd:
                    if (_stack.Count == 0 || _stack.Peek().Array == null)
                    {
                        continue;
                    }

                    value = _stack.Pop().Array;
                    break;
                case PdfTokenKind.DictionaryEnd:
                    if (_stack.Count == 0 || _stack.Peek().Dictionary == null)
                    {
                        continue;
                    }

                    value = _stack.Pop().Dictionary;
                    break;
                case PdfTokenKind.Integer:
                    value = ReadIntegerOrReference(token);
                    break;
                case PdfTokenKind.Real:
                    value = new PdfReal(token.Real);
                    break;
                case PdfTokenKind.LiteralString:
                    value = new PdfString(token.Bytes);
                    break;
                case PdfTokenKind.HexString:
                    value = new PdfString(token.Bytes, true);
                    break;
                case PdfTokenKind.Name:
                    value = new PdfName(token.Text);
                    break;
                case PdfTokenKind.Keyword:
                    if (IsValueKeyword(token.Text, out value))
                    {
                        break;
                    }

                    if (KeywordsEndObjects || IsStructureKeyword(token.Text))
                    {
                        _lexer.Position = token.Start;
                        return Unwind();
                    }

                    value = PdfNull.Value;
                    break;
                default:
                    continue;
            }

            if (_stack.Count == 0)
            {
                return value;
            }

            Place(value);
        }
    }

    /// <summary>Reads <c>N G obj ... endobj</c> at <paramref name="offset"/>, with the stream after a dictionary.
    /// Returns null when there is no object header there.</summary>
    public PdfIndirectObject? ReadIndirectObject(int offset)
    {
        _lexer.Position = offset;
        var number = _lexer.Next();
        var generation = _lexer.Next();
        var keyword = _lexer.Next();
        if (number.Kind != PdfTokenKind.Integer || generation.Kind != PdfTokenKind.Integer ||
            !keyword.IsKeyword(PdfKeywords.Obj) || number.Integer < 0 || number.Integer > int.MaxValue || generation.Integer < 0 ||
            generation.Integer > ushort.MaxValue)
        {
            return null;
        }

        var id = new PdfObjectId((int)number.Integer, (int)generation.Integer);
        var value = ReadObject() ?? PdfNull.Value;
        var next = _lexer.Peek();
        if (value is PdfDictionary dictionary && next.IsKeyword(PdfKeywords.Stream))
        {
            _lexer.Position = next.End;
            var stream = ReadStreamData(dictionary);
            stream.Id = id;
            value = stream;
            next = _lexer.Peek();
        }

        if (next.IsKeyword(PdfKeywords.EndObj))
        {
            _lexer.Position = next.End;
        }

        return new PdfIndirectObject(id, value, number.Start, _lexer.Position);
    }

    private PdfStream ReadStreamData(PdfDictionary dictionary)
    {
        var data = _lexer.Data.Span;
        var start = _lexer.Position;
        if (start < data.Length && data[start] == '\r')
        {
            start++;
        }

        if (start < data.Length && data[start] == '\n')
        {
            start++;
        }

        var length = DeclaredLength(dictionary);
        int end;
        if (length.HasValue && length.Value >= 0 && start + length.Value <= data.Length &&
            EndStreamFollows(data, start + (int)length.Value))
        {
            end = start + (int)length.Value;
        }
        else
        {
            end = FindEndStream(data, start);
        }

        _lexer.Position = end;
        var endStream = _lexer.Peek();
        if (endStream.IsKeyword(PdfKeywords.EndStream))
        {
            _lexer.Position = endStream.End;
        }

        return new PdfStream(dictionary, _lexer.Data[start..end]);
    }

    private long? DeclaredLength(PdfDictionary dictionary)
    {
        return dictionary[PdfKeys.Length] switch
        {
            PdfInteger integer => integer.Value,
            PdfReal real => (long)real.Value,
            PdfReference reference => _lengths?.ResolveLength(reference),
            _ => null,
        };
    }

    /// <summary>Whether <c>endstream</c> follows <paramref name="position"/>, white space aside.</summary>
    internal static bool EndStreamFollows(ReadOnlySpan<byte> data, int position)
    {
        while (position < data.Length && PdfCharacters.IsWhiteSpace(data[position]))
        {
            position++;
        }

        return data[position..].StartsWith(PdfKeywords.EndStreamBytes);
    }

    /// <summary>Where the data ends when its length is wrong: before the end-of-line marker ahead of
    /// <c>endstream</c>, or before the next <c>endobj</c>, or at the end of the file.</summary>
    private int FindEndStream(ReadOnlySpan<byte> data, int start)
    {
        var found = _endStream.Find(data, start);
        if (found < 0)
        {
            found = _endObj.Find(data, start);
        }

        if (found < 0)
        {
            return data.Length;
        }

        var end = found;
        if (end > start && data[end - 1] == '\n')
        {
            end--;
        }

        if (end > start && data[end - 1] == '\r')
        {
            end--;
        }

        return end;
    }

    private PdfObject ReadIntegerOrReference(PdfToken token)
    {
        if (token.Integer < 0 || token.Integer > int.MaxValue)
        {
            return new PdfInteger(token.Integer);
        }

        var position = _lexer.Position;
        var generation = _lexer.Next();
        if (generation.Kind == PdfTokenKind.Integer && generation.Integer >= 0 && generation.Integer <= ushort.MaxValue)
        {
            var keyword = _lexer.Next();
            if (keyword.IsKeyword(PdfKeywords.Reference))
            {
                return new PdfReference((int)token.Integer, (int)generation.Integer);
            }
        }

        _lexer.Position = position;
        return new PdfInteger(token.Integer);
    }

    private void Push(PdfParserFrame frame)
    {
        if (_stack.Count >= MaxDepth)
        {
            throw new PdfException("Arrays and dictionaries nest deeper than " + MaxDepth + ".");
        }

        _stack.Push(frame);
    }

    private void Place(PdfObject value)
    {
        var top = _stack.Peek();
        if (top.Array != null)
        {
            top.Array.Add(value);
            return;
        }

        if (top.Key == null)
        {
            if (value is PdfName name)
            {
                top.Key = name.Value;
            }

            return;
        }

        top.Dictionary.Set(top.Key, value);
        top.Key = null;
    }

    private PdfObject Unwind()
    {
        if (_stack.Count == 0)
        {
            return null;
        }

        while (_stack.Count > 1)
        {
            Place(_stack.Pop().Value);
        }

        return _stack.Pop().Value;
    }

    private static bool IsValueKeyword(string keyword, out PdfObject value)
    {
        value = keyword switch
        {
            PdfKeywords.True => PdfBoolean.True,
            PdfKeywords.False => PdfBoolean.False,
            PdfKeywords.Null => PdfNull.Value,
            _ => null,
        };
        return value != null;
    }

    private static bool IsStructureKeyword(string keyword) =>
        keyword is PdfKeywords.Obj or PdfKeywords.EndObj or PdfKeywords.Stream or PdfKeywords.EndStream or
            PdfKeywords.Xref or PdfKeywords.Trailer or PdfKeywords.StartXref;
}
