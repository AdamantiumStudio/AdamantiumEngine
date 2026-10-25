using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Adamantium.Imaging.Tiff;

/// <summary>GDAL's tags: the value meaning "no data" (GDAL_NODATA, text) and its metadata (GDAL_METADATA, XML of
/// items, each of the dataset or of one band; an item of a named domain is kept as "domain:name").</summary>
internal static partial class TiffGdalMetadata
{
    /// <summary>The no-data value of <paramref name="directory"/>; null when it has none or it is not a number.
    /// Infinities and NaN are read as C runtimes print them, MSVC's "1.#INF" too.</summary>
    public static double? NoData(TiffDirectory directory)
    {
        var text = directory[TiffTag.GdalNoData]?.GetString().Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var special = Special().Match(text);
        if (special.Success)
        {
            return special.Groups["inf"].Success
                ? text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity
                : double.NaN;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    // inf, nan, nan(ind) as C runtimes print them; MSVC's 1.#INF, 1.#QNAN, 1.#SNAN, 1.#IND, with zeros after.
    [GeneratedRegex(@"^[+-]?(?:(?<inf>inf|1\.#inf0*)|nan(?:\([a-z0-9_]*\))?|1\.#(?:qnan|snan|ind)0*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Special();

    /// <summary>The no-data value of <paramref name="directory"/> as an integer of 64 bits, signed or not, when its
    /// text is one exactly - a double cannot hold every such value.</summary>
    public static (long? Signed, ulong? Unsigned) IntegerNoData(TiffDirectory directory)
    {
        var text = directory[TiffTag.GdalNoData]?.GetString().Trim();
        return (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed)
                ? signed
                : null,
            ulong.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var unsigned)
                ? unsigned
                : null);
    }

    /// <summary>The dataset's items and each of <paramref name="bands"/> bands' information; a file without the tag,
    /// or whose XML is broken, gives none and defaults. Items without a name or a value, or of a band that is not
    /// there, are passed over, as GDAL passes them.</summary>
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
            if (string.IsNullOrEmpty(name) || item.Value.Length == 0)
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
