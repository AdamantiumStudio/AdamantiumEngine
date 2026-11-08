using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Content;

/// <summary>The abbreviations of inline images (ISO 32000-2, tables 91 and 92) and their full forms.</summary>
internal static class PdfInlineImageKeys
{
    private static readonly Dictionary<string, string> Keys = new()
    {
        ["BPC"] = "BitsPerComponent",
        ["CS"] = "ColorSpace",
        ["D"] = "Decode",
        ["DP"] = "DecodeParms",
        ["F"] = "Filter",
        ["H"] = "Height",
        ["IM"] = "ImageMask",
        ["I"] = "Interpolate",
        ["W"] = "Width",
        ["L"] = "Length",
    };

    private static readonly Dictionary<string, string> Names = new()
    {
        ["G"] = "DeviceGray",
        ["RGB"] = "DeviceRGB",
        ["CMYK"] = "DeviceCMYK",
        ["I"] = "Indexed",
        ["AHx"] = "ASCIIHexDecode",
        ["A85"] = "ASCII85Decode",
        ["LZW"] = "LZWDecode",
        ["Fl"] = "FlateDecode",
        ["RL"] = "RunLengthDecode",
        ["CCF"] = "CCITTFaxDecode",
        ["DCT"] = "DCTDecode",
    };

    public static string Key(string key) => Keys.GetValueOrDefault(key, key);

    /// <summary>The value with abbreviated names written out: a name, or the names in an array.</summary>
    public static PdfObject Value(string key, PdfObject value)
    {
        if (key is not ("ColorSpace" or "Filter"))
        {
            return value;
        }

        if (value is PdfName name)
        {
            return Names.TryGetValue(name.Value, out var full) ? new PdfName(full) : name;
        }

        if (value is not PdfArray array)
        {
            return value;
        }

        var expanded = new PdfArray();
        foreach (var item in array)
        {
            expanded.Add(item is PdfName part && Names.TryGetValue(part.Value, out var full) ? new PdfName(full) : item);
        }

        return expanded;
    }
}
