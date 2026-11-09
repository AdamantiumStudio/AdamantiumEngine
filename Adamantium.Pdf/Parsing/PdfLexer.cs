using System;
using System.Collections.Generic;
using System.Text;

namespace Adamantium.Pdf.Parsing;

/// <summary>Splits PDF syntax into tokens (ISO 32000-2, 7.2 and 7.3). Lenient as readers must be: a malformed number
/// reads as far as it can, an unterminated string ends with the data, stray closing delimiters are skipped.</summary>
internal sealed class PdfLexer
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private static readonly string[] SyntaxKeywords =
    [
        PdfKeywords.Reference, PdfKeywords.Free, PdfKeywords.InUse, PdfKeywords.Obj, PdfKeywords.EndObj,
        PdfKeywords.Null, PdfKeywords.True, PdfKeywords.False, PdfKeywords.Xref, PdfKeywords.Stream,
        PdfKeywords.EndStream, PdfKeywords.Trailer, PdfKeywords.StartXref,
    ];

    private readonly ReadOnlyMemory<byte> _data;
    private readonly List<byte> _buffer = [];

    public PdfLexer(ReadOnlyMemory<byte> data)
    {
        _data = data;
    }

    public ReadOnlyMemory<byte> Data => _data;

    public int Length => _data.Length;

    public int Position { get; set; }

    public PdfToken Next()
    {
        var data = _data.Span;
        while (true)
        {
            SkipWhiteSpaceAndComments(data);
            if (Position >= data.Length)
            {
                return new PdfToken(PdfTokenKind.EndOfData, data.Length, data.Length);
            }

            var start = Position;
            var current = data[Position];
            switch (current)
            {
                case (byte)'/':
                    return ReadName(data);
                case (byte)'(':
                    return ReadLiteralString(data);
                case (byte)'<':
                    if (Position + 1 < data.Length && data[Position + 1] == '<')
                    {
                        Position += 2;
                        return new PdfToken(PdfTokenKind.DictionaryStart, start, Position);
                    }

                    return ReadHexString(data);
                case (byte)'>':
                    if (Position + 1 < data.Length && data[Position + 1] == '>')
                    {
                        Position += 2;
                        return new PdfToken(PdfTokenKind.DictionaryEnd, start, Position);
                    }

                    Position++;
                    continue;
                case (byte)'[':
                    Position++;
                    return new PdfToken(PdfTokenKind.ArrayStart, start, Position);
                case (byte)']':
                    Position++;
                    return new PdfToken(PdfTokenKind.ArrayEnd, start, Position);
                case (byte)'{':
                    Position++;
                    return new PdfToken(PdfTokenKind.ProcedureStart, start, Position);
                case (byte)'}':
                    Position++;
                    return new PdfToken(PdfTokenKind.ProcedureEnd, start, Position);
                case (byte)')':
                    Position++;
                    continue;
                default:
                    return ReadRegular(data);
            }
        }
    }

    public PdfToken Peek()
    {
        var position = Position;
        var token = Next();
        Position = position;
        return token;
    }

    /// <summary>Skips white space and comments, then a single end-of-line marker at most.</summary>
    public void SkipWhiteSpaceAndComments() => SkipWhiteSpaceAndComments(_data.Span);

    private void SkipWhiteSpaceAndComments(ReadOnlySpan<byte> data)
    {
        while (Position < data.Length)
        {
            var current = data[Position];
            if (PdfCharacters.IsWhiteSpace(current))
            {
                Position++;
            }
            else if (current == '%')
            {
                while (Position < data.Length && data[Position] != '\r' && data[Position] != '\n')
                {
                    Position++;
                }
            }
            else
            {
                return;
            }
        }
    }

    private PdfToken ReadName(ReadOnlySpan<byte> data)
    {
        var start = Position;
        Position++;
        _buffer.Clear();
        while (Position < data.Length && PdfCharacters.IsRegular(data[Position]))
        {
            var current = data[Position];
            if (current == '#' && Position + 2 < data.Length)
            {
                var high = PdfCharacters.HexValue(data[Position + 1]);
                var low = PdfCharacters.HexValue(data[Position + 2]);
                if (high >= 0 && low >= 0)
                {
                    _buffer.Add((byte)((high << 4) | low));
                    Position += 3;
                    continue;
                }
            }

            _buffer.Add(current);
            Position++;
        }

        return new PdfToken(PdfTokenKind.Name, start, Position, text: NameText());
    }

    private string NameText()
    {
        var bytes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
        var ascii = true;
        foreach (var value in bytes)
        {
            if (value >= 0x80)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            return Encoding.ASCII.GetString(bytes);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private PdfToken ReadLiteralString(ReadOnlySpan<byte> data)
    {
        var start = Position;
        Position++;
        _buffer.Clear();
        var depth = 1;
        while (Position < data.Length)
        {
            var current = data[Position++];
            if (current == '(')
            {
                depth++;
                _buffer.Add(current);
            }
            else if (current == ')')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }

                _buffer.Add(current);
            }
            else if (current == '\\')
            {
                ReadEscape(data);
            }
            else if (current == '\r')
            {
                if (Position < data.Length && data[Position] == '\n')
                {
                    Position++;
                }

                _buffer.Add((byte)'\n');
            }
            else
            {
                _buffer.Add(current);
            }
        }

        return new PdfToken(PdfTokenKind.LiteralString, start, Position, bytes: [.. _buffer]);
    }

    private void ReadEscape(ReadOnlySpan<byte> data)
    {
        if (Position >= data.Length)
        {
            return;
        }

        var current = data[Position++];
        switch (current)
        {
            case (byte)'n':
                _buffer.Add((byte)'\n');
                return;
            case (byte)'r':
                _buffer.Add((byte)'\r');
                return;
            case (byte)'t':
                _buffer.Add((byte)'\t');
                return;
            case (byte)'b':
                _buffer.Add((byte)'\b');
                return;
            case (byte)'f':
                _buffer.Add((byte)'\f');
                return;
            case (byte)'\r':
                if (Position < data.Length && data[Position] == '\n')
                {
                    Position++;
                }

                return;
            case (byte)'\n':
                return;
        }

        if (current >= '0' && current <= '7')
        {
            var value = current - '0';
            for (var i = 0; i < 2 && Position < data.Length && data[Position] >= '0' && data[Position] <= '7'; i++)
            {
                value = (value << 3) | (data[Position++] - '0');
            }

            _buffer.Add((byte)value);
            return;
        }

        _buffer.Add(current);
    }

    private PdfToken ReadHexString(ReadOnlySpan<byte> data)
    {
        var start = Position;
        Position++;
        _buffer.Clear();
        var high = -1;
        while (Position < data.Length)
        {
            var current = data[Position++];
            if (current == '>')
            {
                break;
            }

            var value = PdfCharacters.HexValue(current);
            if (value < 0)
            {
                continue;
            }

            if (high < 0)
            {
                high = value;
            }
            else
            {
                _buffer.Add((byte)((high << 4) | value));
                high = -1;
            }
        }

        if (high >= 0)
        {
            _buffer.Add((byte)(high << 4));
        }

        return new PdfToken(PdfTokenKind.HexString, start, Position, bytes: [.. _buffer]);
    }

    private PdfToken ReadRegular(ReadOnlySpan<byte> data)
    {
        var start = Position;
        while (Position < data.Length && PdfCharacters.IsRegular(data[Position]))
        {
            Position++;
        }

        var text = data[start..Position];
        if (TryNumber(text, out var integer, out var real, out var isInteger))
        {
            return isInteger
                ? new PdfToken(PdfTokenKind.Integer, start, Position, integer, integer)
                : new PdfToken(PdfTokenKind.Real, start, Position, (long)real, real);
        }

        return new PdfToken(PdfTokenKind.Keyword, start, Position, text: Keyword(text));
    }

    /// <summary>Reads a number the way readers do: signs repeated or doubled count once, digits after a second point
    /// are dropped. Anything that does not start like a number is a keyword.</summary>
    private static bool TryNumber(ReadOnlySpan<byte> text, out long integer, out double real, out bool isInteger)
    {
        integer = 0;
        real = 0;
        isInteger = true;
        var position = 0;
        var negative = false;
        while (position < text.Length && (text[position] == '-' || text[position] == '+'))
        {
            negative |= text[position] == '-';
            position++;
        }

        if (position == text.Length || (!PdfCharacters.IsDigit(text[position]) && text[position] != '.'))
        {
            return false;
        }

        double whole = 0;
        var digits = 0;
        while (position < text.Length && PdfCharacters.IsDigit(text[position]))
        {
            whole = whole * 10 + (text[position] - '0');
            if (integer <= (long.MaxValue - 9) / 10)
            {
                integer = integer * 10 + (text[position] - '0');
            }
            else
            {
                isInteger = false;
            }

            digits++;
            position++;
        }

        var fractionDigits = 0;
        if (position < text.Length && text[position] == '.')
        {
            isInteger = false;
            position++;
            while (position < text.Length && PdfCharacters.IsDigit(text[position]))
            {
                if (fractionDigits < 18)
                {
                    whole = whole * 10 + (text[position] - '0');
                    fractionDigits++;
                }

                digits++;
                position++;
            }
        }

        if (digits == 0)
        {
            return !isInteger;
        }

        real = fractionDigits == 0 ? whole : whole / Math.Pow(10, fractionDigits);
        if (negative)
        {
            real = -real;
            integer = -integer;
        }

        return true;
    }

    private static string Keyword(ReadOnlySpan<byte> text)
    {
        foreach (var keyword in SyntaxKeywords)
        {
            if (Spells(text, keyword))
            {
                return keyword;
            }
        }

        return Encoding.Latin1.GetString(text);
    }

    private static bool Spells(ReadOnlySpan<byte> text, string keyword)
    {
        if (text.Length != keyword.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != keyword[i])
            {
                return false;
            }
        }

        return true;
    }
}
