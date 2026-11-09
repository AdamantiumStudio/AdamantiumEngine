using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>Splits the PostScript of a Type 1 font into tokens, enough to find its dictionaries; binary charstrings are
/// taken by <see cref="ReadBinary"/> where the font says they start.</summary>
internal sealed class Type1Tokenizer
{
    private readonly byte[] _data;
    private readonly int _end;

    public Type1Tokenizer(byte[] data, int start = 0, int end = -1)
    {
        _data = data;
        Position = start;
        _end = end < 0 ? data.Length : Math.Min(end, data.Length);
    }

    public int Position { get; set; }

    public Type1Token Next()
    {
        SkipWhitespaceAndComments();
        if (Position >= _end)
        {
            return new Type1Token(Type1TokenKind.End, null);
        }

        var b = _data[Position];
        switch (b)
        {
            case (byte)'[':
            case (byte)'{':
                Position++;
                return new Type1Token(Type1TokenKind.Open, ((char)b).ToString());
            case (byte)']':
            case (byte)'}':
                Position++;
                return new Type1Token(Type1TokenKind.Close, ((char)b).ToString());
            case (byte)'(':
                return ReadString();
            case (byte)'<':
                return ReadHexString();
            case (byte)'/':
                Position++;
                return new Type1Token(Type1TokenKind.Name, ReadRegular());
        }

        var text = ReadRegular();
        if (text.Length == 0)
        {
            Position++;
            return new Type1Token(Type1TokenKind.Keyword, ((char)b).ToString());
        }

        return TryNumber(text, out var number)
            ? new Type1Token(Type1TokenKind.Number, text, number)
            : new Type1Token(Type1TokenKind.Keyword, text);
    }

    /// <summary>The <paramref name="length"/> bytes that follow the single space after an RD token.</summary>
    /// <exception cref="InvalidDataException">They run past the end of the font.</exception>
    public byte[] ReadBinary(double length)
    {
        Position++;
        if (length < 0 || Position > _end || length > _end - Position)
        {
            throw new InvalidDataException("A charstring runs past the end of the font.");
        }

        var count = (int)length;
        var bytes = new byte[count];
        Buffer.BlockCopy(_data, Position, bytes, 0, count);
        Position += count;
        return bytes;
    }

    private void SkipWhitespaceAndComments()
    {
        while (Position < _end)
        {
            var b = _data[Position];
            if (IsWhitespace(b))
            {
                Position++;
            }
            else if (b == '%')
            {
                while (Position < _end && _data[Position] != '\r' && _data[Position] != '\n')
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

    private string ReadRegular()
    {
        var start = Position;
        while (Position < _end && !IsWhitespace(_data[Position]) && !IsDelimiter(_data[Position]))
        {
            Position++;
        }

        return Encoding.ASCII.GetString(_data, start, Position - start);
    }

    private Type1Token ReadString()
    {
        Position++;
        var start = Position;
        var depth = 1;
        while (Position < _end && depth > 0)
        {
            var b = _data[Position++];
            if (b == '\\')
            {
                Position++;
            }
            else if (b == '(')
            {
                depth++;
            }
            else if (b == ')')
            {
                depth--;
            }
        }

        var length = Math.Max(0, Math.Min(Position, _end) - start - (depth == 0 ? 1 : 0));
        return new Type1Token(Type1TokenKind.String, Encoding.ASCII.GetString(_data, start, length));
    }

    private Type1Token ReadHexString()
    {
        Position++;
        var start = Position;
        while (Position < _end && _data[Position] != '>')
        {
            Position++;
        }

        var text = Encoding.ASCII.GetString(_data, start, Position - start);
        if (Position < _end)
        {
            Position++;
        }

        return new Type1Token(Type1TokenKind.String, text);
    }

    private static bool TryNumber(string text, out double number)
    {
        var hash = text.IndexOf('#');
        if (hash > 0)
        {
            number = 0;
            if (!int.TryParse(text.AsSpan(0, hash), NumberStyles.None, CultureInfo.InvariantCulture, out var radix) ||
                radix < 2 || radix > 36)
            {
                return false;
            }

            for (var i = hash + 1; i < text.Length; i++)
            {
                var digit = char.IsDigit(text[i]) ? text[i] - '0' : char.ToUpperInvariant(text[i]) - 'A' + 10;
                if (digit < 0 || digit >= radix)
                {
                    return false;
                }

                number = number * radix + digit;
            }

            return hash + 1 < text.Length;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
               !double.IsNaN(number) && !double.IsInfinity(number);
    }

    internal static bool IsWhitespace(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or
        (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
}
