using System.Collections.Generic;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Content;

/// <summary>The abbreviations of inline images (ISO 32000-2, tables 91 and 92) and their full forms.</summary>
internal static class PdfInlineImageKeys
{
    private static readonly Dictionary<string, string> Keys = new()
    {
        [PdfAbbreviations.BitsPerComponent] = PdfKeys.BitsPerComponent,
        [PdfAbbreviations.ColorSpace] = PdfKeys.ColorSpace,
        [PdfAbbreviations.Decode] = PdfKeys.Decode,
        [PdfAbbreviations.DecodeParms] = PdfKeys.DecodeParms,
        [PdfAbbreviations.Filter] = PdfKeys.Filter,
        [PdfAbbreviations.Height] = PdfKeys.Height,
        [PdfAbbreviations.ImageMask] = PdfKeys.ImageMask,
        [PdfAbbreviations.Interpolate] = PdfKeys.Interpolate,
        [PdfAbbreviations.Width] = PdfKeys.Width,
        [PdfAbbreviations.Length] = PdfKeys.Length,
    };

    private static readonly Dictionary<string, string> Names = new()
    {
        [PdfAbbreviations.DeviceGray] = PdfColorSpaceFamilies.DeviceGray,
        [PdfAbbreviations.DeviceRGB] = PdfColorSpaceFamilies.DeviceRGB,
        [PdfAbbreviations.DeviceCMYK] = PdfColorSpaceFamilies.DeviceCMYK,
        [PdfAbbreviations.Indexed] = PdfColorSpaceFamilies.Indexed,
        [PdfAbbreviations.ASCIIHexDecode] = PdfFilterNames.ASCIIHexDecode,
        [PdfAbbreviations.ASCII85Decode] = PdfFilterNames.ASCII85Decode,
        [PdfAbbreviations.LZWDecode] = PdfFilterNames.LZWDecode,
        [PdfAbbreviations.FlateDecode] = PdfFilterNames.FlateDecode,
        [PdfAbbreviations.RunLengthDecode] = PdfFilterNames.RunLengthDecode,
        [PdfAbbreviations.CCITTFaxDecode] = PdfFilterNames.CCITTFaxDecode,
        [PdfAbbreviations.DCTDecode] = PdfFilterNames.DCTDecode,
    };

    public static string Key(string key) => Keys.GetValueOrDefault(key, key);

    /// <summary>The value with abbreviated names written out: a name, or the names in an array.</summary>
    public static PdfObject Value(string key, PdfObject value)
    {
        if (key is not (PdfKeys.ColorSpace or PdfKeys.Filter))
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
