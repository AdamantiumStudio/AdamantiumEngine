using System;
using System.Text;

namespace Adamantium.Pdf.Objects;

/// <summary>Text strings (ISO 32000-2, 7.9.2.2): UTF-16BE or UTF-8 after a byte order mark, PDFDocEncoding otherwise.
/// </summary>
public static class PdfTextEncoding
{
    private static readonly char[] DocEncodingHigh =
    [
        '•', '†', '‡', '…', '—', '–', 'ƒ', '⁄',
        '‹', '›', '−', '‰', '„', '“', '”', '‘',
        '’', '‚', '™', 'ﬁ', 'ﬂ', 'Ł', 'Œ', 'Š',
        'Ÿ', 'Ž', 'ı', 'ł', 'œ', 'š', 'ž', '�',
        '€',
    ];

    private static readonly char[] DocEncodingAccents =
    [
        '˘', 'ˇ', 'ˆ', '˙', '˝', '˛', '˚', '˜',
    ];

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        var chars = bytes.Length <= 256 ? stackalloc char[bytes.Length] : new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = FromDocEncoding(bytes[i]);
        }

        return new string(chars);
    }

    /// <summary>The bytes of <paramref name="text"/> as a text string: PDFDocEncoding when every character has a byte
    /// there and the bytes do not begin as a byte order mark would, UTF-16BE after its byte order mark otherwise.
    /// </summary>
    public static byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (!TryToDocEncoding(text[i], out bytes[i]))
            {
                return Unicode(text);
            }
        }

        return bytes is [0xFE, 0xFF, ..] or [0xEF, 0xBB, 0xBF, ..] ? Unicode(text) : bytes;
    }

    private static byte[] Unicode(string text)
    {
        var unicode = new byte[2 + 2 * text.Length];
        (unicode[0], unicode[1]) = (0xFE, 0xFF);
        Encoding.BigEndianUnicode.GetBytes(text, unicode.AsSpan(2));
        return unicode;
    }

    private static bool TryToDocEncoding(char character, out byte value)
    {
        value = 0;
        if (character is (>= ' ' and < (char)0x7F) or '\t' or '\n' or '\r' ||
            character is >= (char)0xA1 and <= (char)0xFF and not (char)0xAD)
        {
            value = (byte)character;
            return true;
        }

        var high = Array.IndexOf(DocEncodingHigh, character);
        if (high >= 0 && character != '�')
        {
            value = (byte)(0x80 + high);
            return true;
        }

        var accent = Array.IndexOf(DocEncodingAccents, character);
        if (accent >= 0)
        {
            value = (byte)(0x18 + accent);
            return true;
        }

        return false;
    }

    /// <summary>The character a PDFDocEncoding byte stands for.</summary>
    public static char FromDocEncoding(byte value)
    {
        if (value >= 0x18 && value <= 0x1F)
        {
            return DocEncodingAccents[value - 0x18];
        }

        if (value >= 0x80 && value <= 0xA0)
        {
            return DocEncodingHigh[value - 0x80];
        }

        return (char)value;
    }
}
