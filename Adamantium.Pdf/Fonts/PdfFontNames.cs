using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.Core.Shared.Fonts;
using Adamantium.Fonts;

namespace Adamantium.Pdf.Fonts;

/// <summary>What a font's PostScript name says: its family, weight and slant, and which of the 14 standard fonts
/// (ISO 32000-2, 9.6.2.2) it is.</summary>
internal static class PdfFontNames
{
    public const string Symbol = "Symbol";
    public const string ZapfDingbats = "ZapfDingbats";

    private static readonly string[] Sans = ["Helvetica", "Arial", "Liberation Sans", "Nimbus Sans", "Arimo"];
    private static readonly string[] Serif = ["Times", "Times New Roman", "Liberation Serif", "Nimbus Roman", "Tinos"];
    private static readonly string[] Mono = ["Courier", "Courier New", "Liberation Mono", "Nimbus Mono PS", "Cousine"];
    private static readonly string[] Symbols = ["Symbol", "Standard Symbols PS"];
    private static readonly string[] Dingbats = ["ZapfDingbats", "Zapf Dingbats", "D050000L"];

    public static string WithoutSubsetTag(string name)
    {
        if (name is { Length: > 7 } && name[6] == '+')
        {
            for (var i = 0; i < 6; i++)
            {
                if (name[i] is < 'A' or > 'Z')
                {
                    return name;
                }
            }

            return name[7..];
        }

        return name;
    }

    /// <summary>The family a name is of, without its style: <c>Arial,BoldItalic</c> and <c>Arial-BoldItalicMT</c>
    /// are Arial, <c>TimesNewRomanPSMT</c> is Times New Roman.</summary>
    public static string FamilyOf(string name)
    {
        name = WithoutSubsetTag(name) ?? string.Empty;
        var cut = name.IndexOfAny([',', '-']);
        var family = cut >= 0 ? name[..cut] : name;
        foreach (var suffix in new[] { "PSMT", "MT", "PS" })
        {
            if (family.Length > suffix.Length && family.EndsWith(suffix, StringComparison.Ordinal))
            {
                family = family[..^suffix.Length];
                break;
            }
        }

        return SpaceWords(family);
    }

    public static FontWeight WeightOf(string name, PdfFontDescriptor descriptor)
    {
        var style = StyleWords(name);
        if (style.Contains("black", StringComparison.Ordinal) || style.Contains("heavy", StringComparison.Ordinal))
        {
            return FontWeight.Black;
        }

        if (style.Contains("semibold", StringComparison.Ordinal) ||
            style.Contains("demi", StringComparison.Ordinal))
        {
            return FontWeight.SemiBold;
        }

        if (style.Contains("bold", StringComparison.Ordinal))
        {
            return FontWeight.Bold;
        }

        if (style.Contains("light", StringComparison.Ordinal))
        {
            return FontWeight.Light;
        }

        if (descriptor?.FontWeight is >= 100 and <= 900)
        {
            return new FontWeight(descriptor.FontWeight);
        }

        return descriptor != null && (descriptor.Flags & PdfFontFlags.ForceBold) != 0
            ? FontWeight.Bold
            : FontWeight.Normal;
    }

    public static FontStyle StyleOf(string name, PdfFontDescriptor descriptor)
    {
        var style = StyleWords(name);
        if (style.Contains("italic", StringComparison.Ordinal) ||
            style.Contains("oblique", StringComparison.Ordinal))
        {
            return FontStyle.Italic;
        }

        return descriptor != null && ((descriptor.Flags & PdfFontFlags.Italic) != 0 || descriptor.ItalicAngle != 0)
            ? FontStyle.Italic
            : FontStyle.Normal;
    }

    /// <summary>The families to try for a font the file does not embed: its own first, then those of the same
    /// metrics as the standard font it is or resembles.</summary>
    public static IReadOnlyList<string> CandidatesOf(string name, PdfFontDescriptor descriptor)
    {
        var family = FamilyOf(name);
        var candidates = new List<string>();
        if (family.Length > 0)
        {
            candidates.Add(family);
            var unspaced = family.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (unspaced != family)
            {
                candidates.Add(unspaced);
            }
        }

        candidates.AddRange(StandardFamilies(family, descriptor));
        return candidates;
    }

    private static string[] StandardFamilies(string family, PdfFontDescriptor descriptor)
    {
        var key = family.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (key.StartsWith("courier", StringComparison.Ordinal))
        {
            return Mono;
        }

        if (key.StartsWith("times", StringComparison.Ordinal))
        {
            return Serif;
        }

        if (key.StartsWith("helvetica", StringComparison.Ordinal) || key.StartsWith("arial", StringComparison.Ordinal))
        {
            return Sans;
        }

        if (key == "symbol")
        {
            return Symbols;
        }

        if (key.StartsWith("zapfdingbats", StringComparison.Ordinal) || key == "dingbats")
        {
            return Dingbats;
        }

        var flags = descriptor?.Flags ?? PdfFontFlags.None;
        return (flags & PdfFontFlags.FixedPitch) != 0 ? Mono
            : (flags & PdfFontFlags.Serif) != 0 ? Serif
            : Sans;
    }

    /// <summary>The built-in encoding of the standard Symbol or ZapfDingbats font when the name is one of them; null
    /// for any other.</summary>
    public static IReadOnlyList<string> BuiltInEncodingOf(string name) =>
        FamilyOf(name).Replace(" ", string.Empty, StringComparison.Ordinal) switch
        {
            Symbol => PdfSimpleEncodings.Symbol,
            ZapfDingbats => PdfSimpleEncodings.ZapfDingbats,
            _ => null,
        };

    private static string StyleWords(string name)
    {
        name = WithoutSubsetTag(name) ?? string.Empty;
        var cut = name.IndexOfAny([',', '-']);
        return cut >= 0 ? name[(cut + 1)..].ToLowerInvariant() : string.Empty;
    }

    private static string SpaceWords(string name)
    {
        var spaced = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
            {
                spaced.Append(' ');
            }

            spaced.Append(name[i]);
        }

        return spaced.ToString();
    }
}
