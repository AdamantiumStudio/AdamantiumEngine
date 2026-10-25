using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Adamantium.Imaging.Tiff;

/// <summary>GDAL's tags: the value meaning "no data" (GDAL_NODATA, text) and its metadata (GDAL_METADATA, XML of
/// items, each of the dataset or of one band; an item of a named domain is kept as "domain:name").</summary>
internal static class TiffGdalMetadata
{
    /// <summary>The no-data value of <paramref name="directory"/>; null when it has none or it is not a number.
    /// </summary>
    public static double? NoData(TiffDirectory directory)
    {
        var text = directory[TiffTag.GdalNoData]?.GetString().Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.ToLowerInvariant() switch
        {
            "nan" => double.NaN,
            "inf" or "+inf" => double.PositiveInfinity,
            "-inf" => double.NegativeInfinity,
            _ => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null
        };
    }

    /// <summary>The dataset's items and each of <paramref name="bands"/> bands' information; a file without the tag,
    /// or whose XML is broken, gives none and defaults. Items without a name, or of a band that is not there, are
    /// passed over.</summary>
    public static (IReadOnlyDictionary<string, string> Dataset, TiffBandInfo[] Bands) Read(TiffDirectory directory,
        int bands)
    {
        var dataset = new Dictionary<string, string>();
        var perBand = Enumerable.Range(0, bands).Select(_ => new Dictionary<string, string>()).ToArray();
        var roles = Enumerable.Range(0, bands).Select(_ => new Dictionary<string, string>()).ToArray();
        foreach (var item in Items(directory?[TiffTag.GdalMetadata]?.GetString()))
        {
            var name = (string)item.Attribute("name");
            var domain = (string)item.Attribute("domain");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var key = string.IsNullOrEmpty(domain) ? name : domain + ":" + name;
            var sample = (string)item.Attribute("sample");
            if (sample == null)
            {
                dataset[key] = item.Value;
            }
            else if (int.TryParse(sample, NumberStyles.None, CultureInfo.InvariantCulture, out var band) && band < bands)
            {
                perBand[band][key] = item.Value;
                if (string.IsNullOrEmpty(domain))
                {
                    roles[band][((string)item.Attribute("role") ?? name).ToLowerInvariant()] = item.Value;
                }
            }
        }

        var info = perBand.Select((items, band) => new TiffBandInfo(
            Number(roles[band], "scale", 1), Number(roles[band], "offset", 0),
            roles[band].GetValueOrDefault("description", ""), items.AsReadOnly())).ToArray();
        return (dataset.AsReadOnly(), info);
    }

    private static IEnumerable<XElement> Items(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return XDocument.Parse(text).Root?.Elements("Item").ToList() ?? [];
        }
        catch (XmlException)
        {
            return [];
        }
    }

    private static double Number(Dictionary<string, string> roles, string role, double fallback) =>
        roles.TryGetValue(role, out var text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
