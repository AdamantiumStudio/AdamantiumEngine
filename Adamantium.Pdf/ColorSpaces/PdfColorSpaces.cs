using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>Reads color spaces out of a file (ISO 32000-2, 8.6): by name, from the resources, or from an array. Spaces
/// nest - an Indexed base, a Separation alternate - no deeper than <see cref="MaxDepth"/>. A space that cannot be read
/// is null, and the caller falls back to DeviceGray.</summary>
public static class PdfColorSpaces
{
    public const int MaxDepth = 8;

    private static readonly double[] D65 = [0.9505, 1, 1.089];

    public static PdfColorSpace Create(PdfObject value, PdfResources resources, PdfDocument document) =>
        Create(value, resources, document, 0);

    private static PdfColorSpace Create(PdfObject value, PdfResources resources, PdfDocument document, int depth)
    {
        if (depth > MaxDepth)
        {
            return null;
        }

        value = document.Resolve(value);
        if (value is PdfName name)
        {
            return Named(name.Value, resources, document, depth);
        }

        if (value is not PdfArray { Count: > 0 } array || document.Resolve(array[0]) is not PdfName family)
        {
            return null;
        }

        return family.Value switch
        {
            "DeviceGray" or "G" or "DeviceRGB" or "RGB" or "DeviceCMYK" or "CMYK" =>
                Named(family.Value, null, document, depth),
            "Pattern" => new PdfPatternColorSpace(array.Count > 1 ? Create(array[1], resources, document, depth + 1)
                : null),
            "CalGray" => CalGray(array, document),
            "CalRGB" => CalRgb(array, document),
            "Lab" => Lab(array, document),
            "ICCBased" => IccBased(array, resources, document, depth),
            "Indexed" or "I" => Indexed(array, resources, document, depth),
            "Separation" => Separation(array, resources, document, depth),
            "DeviceN" => DeviceN(array, resources, document, depth),
            _ => null,
        };
    }

    private static PdfColorSpace Named(string name, PdfResources resources, PdfDocument document, int depth)
    {
        var device = name switch
        {
            "DeviceGray" or "G" => "DefaultGray",
            "DeviceRGB" or "RGB" => "DefaultRGB",
            "DeviceCMYK" or "CMYK" => "DefaultCMYK",
            _ => null,
        };
        if (device != null)
        {
            var substitute = resources?.ColorSpace(device);
            if (substitute != null && Create(substitute, null, document, depth + 1) is { } defaulted)
            {
                return defaulted;
            }

            return device switch
            {
                "DefaultGray" => PdfDeviceGrayColorSpace.Instance,
                "DefaultRGB" => PdfDeviceRgbColorSpace.Instance,
                _ => PdfDeviceCmykColorSpace.Instance,
            };
        }

        if (name == "Pattern")
        {
            return new PdfPatternColorSpace(null);
        }

        var named = resources?.ColorSpace(name);
        return named == null ? null : Create(named, resources, document, depth + 1);
    }

    private static PdfDictionary Parameters(PdfArray array, PdfDocument document) =>
        array.Count > 1 ? document.Resolve(array[1]) as PdfDictionary : null;

    private static double[] WhitePoint(PdfDictionary parameters, PdfDocument document)
    {
        var white = PdfValues.Numbers(parameters?["WhitePoint"], document);
        return white is { Length: 3 } && white[1] > 0 ? white : D65;
    }

    private static PdfColorSpace CalGray(PdfArray array, PdfDocument document)
    {
        var parameters = Parameters(array, document);
        return new PdfCalGrayColorSpace(WhitePoint(parameters, document),
            PdfValues.Number(parameters?["Gamma"], document, 1));
    }

    private static PdfColorSpace CalRgb(PdfArray array, PdfDocument document)
    {
        var parameters = Parameters(array, document);
        return new PdfCalRgbColorSpace(WhitePoint(parameters, document),
            PdfValues.Numbers(parameters?["Gamma"], document), PdfValues.Numbers(parameters?["Matrix"], document));
    }

    private static PdfColorSpace Lab(PdfArray array, PdfDocument document)
    {
        var parameters = Parameters(array, document);
        return new PdfLabColorSpace(WhitePoint(parameters, document),
            PdfValues.Numbers(parameters?["Range"], document));
    }

    private static PdfColorSpace IccBased(PdfArray array, PdfResources resources, PdfDocument document, int depth)
    {
        if (array.Count < 2 || document.Resolve(array[1]) is not PdfStream stream)
        {
            return null;
        }

        var dictionary = stream.Dictionary;
        var components = PdfValues.Integer(dictionary["N"], document);
        var alternate = dictionary["Alternate"] != null
            ? Create(dictionary["Alternate"], resources, document, depth + 1)
            : null;
        if (components is not (1 or 3 or 4))
        {
            components = alternate?.Components ?? 0;
        }

        if (components is not (1 or 3 or 4))
        {
            return null;
        }

        if (alternate == null || alternate.Components != components)
        {
            alternate = components switch
            {
                1 => PdfDeviceGrayColorSpace.Instance,
                3 => PdfDeviceRgbColorSpace.Instance,
                _ => PdfDeviceCmykColorSpace.Instance,
            };
        }

        return new PdfIccBasedColorSpace(components, alternate, PdfValues.Numbers(dictionary["Range"], document));
    }

    private static PdfColorSpace Indexed(PdfArray array, PdfResources resources, PdfDocument document, int depth)
    {
        if (array.Count < 4)
        {
            return null;
        }

        var @base = Create(array[1], resources, document, depth + 1);
        if (@base == null || @base is PdfPatternColorSpace or PdfIndexedColorSpace)
        {
            return null;
        }

        var lookup = document.Resolve(array[3]) switch
        {
            PdfString text => text.Bytes,
            PdfStream stream => Decoded(stream, document),
            _ => null,
        };
        return lookup == null
            ? null
            : new PdfIndexedColorSpace(@base, PdfValues.Integer(array[2], document), lookup);
    }

    private static byte[] Decoded(PdfStream stream, PdfDocument document)
    {
        try
        {
            return document.Decode(stream).Data.ToArray();
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static PdfColorSpace Separation(PdfArray array, PdfResources resources, PdfDocument document, int depth)
    {
        if (array.Count < 4)
        {
            return null;
        }

        var colorant = PdfValues.Name(array[1], document) ?? "None";
        var alternate = Create(array[2], resources, document, depth + 1);
        return new PdfSeparationColorSpace(colorant, alternate, PdfFunctions.Create(array[3], document));
    }

    private static PdfColorSpace DeviceN(PdfArray array, PdfResources resources, PdfDocument document, int depth)
    {
        if (array.Count < 4 || document.Resolve(array[1]) is not PdfArray names || names.Count == 0 ||
            names.Count > 32)
        {
            return null;
        }

        var colorants = new List<string>();
        foreach (var item in names)
        {
            colorants.Add(PdfValues.Name(item, document) ?? "None");
        }

        var alternate = Create(array[2], resources, document, depth + 1);
        return new PdfDeviceNColorSpace(colorants, alternate, PdfFunctions.Create(array[3], document));
    }
}
