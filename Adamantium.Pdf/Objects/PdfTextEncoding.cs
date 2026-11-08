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
