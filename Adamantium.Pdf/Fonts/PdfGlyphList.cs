using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Adamantium.Pdf.Fonts;

/// <summary>Adobe's glyph lists, read from the data the assembly carries: the Adobe Glyph List for any font, the ITC
/// Zapf Dingbats list for the ZapfDingbats font. Private-use values are left out: they are no text to find or copy.
/// </summary>
internal static class PdfGlyphList
{
    private const string GlyphListResource = "Adamantium.Pdf.GlyphList.glyphlist.txt";
    private const string DingbatsResource = "Adamantium.Pdf.GlyphList.zapfdingbats.txt";

    private static readonly Lazy<Dictionary<string, string>> Texts = new(() => Read(GlyphListResource));
    private static readonly Lazy<Dictionary<string, string>> DingbatTexts = new(() => Read(DingbatsResource));

    /// <summary>The text of a glyph name the Adobe Glyph List holds, or null.</summary>
    public static string TextOf(string name) => Find(Texts.Value, name);

    /// <summary>The text of a glyph name of the ZapfDingbats font, or null.</summary>
    public static string DingbatTextOf(string name) => Find(DingbatTexts.Value, name);

    private static string Find(Dictionary<string, string> texts, string name) =>
        name != null && texts.TryGetValue(name, out var text) ? text : null;

    private static Dictionary<string, string> Read(string resource)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = typeof(PdfGlyphList).Assembly.GetManifestResourceStream(resource);
        if (stream == null)
        {
            return texts;
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var text = new StringBuilder();
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(';');
            if (line.StartsWith('#') || separator <= 0)
            {
                continue;
            }

            text.Clear();
            var privateUse = false;
            foreach (var code in line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(code, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit))
                {
                    privateUse |= unit is >= 0xE000 and <= 0xF8FF;
                    text.Append((char)unit);
                }
            }

            if (!privateUse && text.Length > 0)
            {
                texts.TryAdd(line[..separator], text.ToString());
            }
        }

        return texts;
    }
}
