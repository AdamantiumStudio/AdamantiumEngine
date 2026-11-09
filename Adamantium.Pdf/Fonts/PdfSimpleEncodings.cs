using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Adamantium.Fonts.Encodings;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>The encodings of simple fonts by code (ISO 32000-2, 9.6.5 and annex D), and the text a glyph name stands
/// for.</summary>
internal static class PdfSimpleEncodings
{
    private static readonly Dictionary<string, char> LatinText = BuildLatinText();

    public static IReadOnlyList<string> Standard => StandardEncoding.GlyphNames;

    public static IReadOnlyList<string> MacRoman { get; } = Latin(c => c.MacRoman, (202, "space"));

    /// <summary>WinAnsiEncoding, its unused codes above 32 the bullet (ISO 32000-1, annex D.2, note 3).</summary>
    public static IReadOnlyList<string> WinAnsi { get; } = BulletsInGaps(
        Latin(c => c.WinAnsi, (160, "space"), (173, "hyphen")));

    public static IReadOnlyList<string> MacExpert { get; } = ByCode(PdfMacExpertEncoding.Codes);

    public static IReadOnlyList<string> Symbol { get; } = ByCode(PdfSymbolEncoding.Codes);

    public static IReadOnlyList<string> ZapfDingbats { get; } = ByCode(PdfZapfDingbatsEncoding.Codes);

    /// <summary>The encoding a font dictionary names, or null for a name it does not define.</summary>
    public static IReadOnlyList<string> Named(string name) => name switch
    {
        PdfEncodingNames.StandardEncoding => Standard,
        PdfEncodingNames.WinAnsiEncoding => WinAnsi,
        PdfEncodingNames.MacRomanEncoding => MacRoman,
        PdfEncodingNames.MacExpertEncoding => MacExpert,
        _ => null,
    };

    /// <summary>The text a glyph name stands for: a name of the Latin character set or of the Adobe Glyph List, a
    /// <c>uniXXXX</c> or <c>uXXXX</c> code, and ligatures of them joined by underscores; whatever follows a period is
    /// a variant and ignored. Null when no part of it is known.</summary>
    public static string TextOf(string glyphName) => TextOf(glyphName, false);

    /// <summary>The text a glyph name stands for in a font, the names of the Zapf Dingbats list first when the font
    /// is ZapfDingbats.</summary>
    public static string TextOf(string glyphName, bool dingbats)
    {
        if (string.IsNullOrEmpty(glyphName))
        {
            return null;
        }

        var period = glyphName.IndexOf('.');
        var name = period >= 0 ? glyphName[..period] : glyphName;
        var text = new StringBuilder();
        foreach (var part in name.Split('_'))
        {
            if (dingbats && PdfGlyphList.DingbatTextOf(part) is { } dingbat)
            {
                text.Append(dingbat);
                continue;
            }

            AppendText(part, text);
        }

        return text.Length > 0 ? text.ToString() : null;
    }

    private static void AppendText(string part, StringBuilder text)
    {
        if (LatinText.TryGetValue(part, out var latin))
        {
            text.Append(latin);
            return;
        }

        if (PdfGlyphList.TextOf(part) is { } listed)
        {
            text.Append(listed);
            return;
        }

        if (part.Length >= 7 && (part.Length - 3) % 4 == 0 && part.StartsWith("uni", StringComparison.Ordinal))
        {
            var start = text.Length;
            for (var at = 3; at < part.Length; at += 4)
            {
                if (!TryHex(part.AsSpan(at, 4), out var unit) || unit is >= 0xD800 and <= 0xDFFF)
                {
                    text.Length = start;
                    return;
                }

                text.Append((char)unit);
            }

            return;
        }

        if (part.Length is >= 5 and <= 7 && part[0] == 'u' && TryHex(part.AsSpan(1), out var scalar) &&
            scalar <= 0x10FFFF && scalar is < 0xD800 or > 0xDFFF)
        {
            text.Append(char.ConvertFromUtf32(scalar));
        }
    }

    private static bool TryHex(ReadOnlySpan<char> digits, out int value)
    {
        value = 0;
        foreach (var digit in digits)
        {
            if (!char.IsAsciiHexDigitUpper(digit))
            {
                return false;
            }
        }

        return int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static string[] Latin(Func<PdfLatinCharacter, short> code, params (int Code, string Name)[] duplicates)
    {
        var names = new string[256];
        foreach (var character in PdfLatinCharacterSet.Characters)
        {
            var at = code(character);
            if (at >= 0)
            {
                names[at] = character.Name;
            }
        }

        foreach (var (at, name) in duplicates)
        {
            names[at] = name;
        }

        return names;
    }

    private static string[] BulletsInGaps(string[] names)
    {
        for (var code = 33; code < names.Length; code++)
        {
            names[code] ??= "bullet";
        }

        return names;
    }

    private static string[] ByCode((int Code, string Name)[] codes)
    {
        var names = new string[256];
        foreach (var (code, name) in codes)
        {
            names[code] = name;
        }

        return names;
    }

    private static Dictionary<string, char> BuildLatinText()
    {
        var text = new Dictionary<string, char>(StringComparer.Ordinal);
        foreach (var character in PdfLatinCharacterSet.Characters)
        {
            text[character.Name] = PdfTextEncoding.FromDocEncoding((byte)character.PdfDoc);
        }

        return text;
    }
}
