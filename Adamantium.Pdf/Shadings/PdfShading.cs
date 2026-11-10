using System;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Shadings;

/// <summary>A shading (ISO 32000-2, 8.7.4.5): a smooth transition of color over an area, from a function of position
/// or between the vertices of a mesh. Its colors are given as values - the color space's components, or the inputs of
/// <see cref="Function"/> when it has one - and <see cref="ToRgb"/> turns them into sRGB.</summary>
public abstract class PdfShading
{
    private protected PdfShading(int type, PdfColorSpace colorSpace, double[] background, PdfRectangle? box,
        bool antiAlias, PdfFunction function)
    {
        Type = type;
        ColorSpace = colorSpace;
        Background = background;
        BBox = box;
        AntiAlias = antiAlias;
        Function = function;
    }

    /// <summary>1 to 7, as <c>/ShadingType</c> says.</summary>
    public int Type { get; }

    public PdfColorSpace ColorSpace { get; }

    /// <summary>The color outside the shaded area, in <see cref="ColorSpace"/>, when <c>sh</c> does not paint it;
    /// null for none.</summary>
    public double[] Background { get; }

    /// <summary>Where the shading is cut, in its own space; null for nowhere.</summary>
    public PdfRectangle? BBox { get; }

    public bool AntiAlias { get; }

    /// <summary>What turns a shading's values into color components; null when the values are the components.</summary>
    public PdfFunction Function { get; }

    /// <summary>How many values a color is given by: the function's inputs, else the space's components.</summary>
    public int ValueCount => Function?.Inputs ?? ColorSpace.Components;

    /// <summary>The sRGB color, 0 to 1 each, of the values <paramref name="values"/>.</summary>
    public void ToRgb(ReadOnlySpan<double> values, Span<double> rgb)
    {
        Span<double> components = stackalloc double[Math.Max(ColorSpace.Components, 1)];
        if (Function != null)
        {
            Span<double> output = stackalloc double[Math.Max(Function.Outputs, components.Length)];
            Function.Evaluate(values, output);
            output[..components.Length].CopyTo(components);
        }
        else
        {
            values[..Math.Min(values.Length, components.Length)].CopyTo(components);
        }

        ColorSpace.ToRgb(components, rgb);
    }

    /// <summary>The shading of a dictionary or stream; null when it is not one - an unknown type, a pattern color space,
    /// entries missing or of the wrong kind.</summary>
    public static PdfShading Create(PdfObject value, PdfResources resources, PdfDocument document)
    {
        var resolved = document.Resolve(value);
        var dictionary = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dictionary == null)
        {
            return null;
        }

        var type = PdfValues.Integer(dictionary[PdfShadingKeys.ShadingType], document);
        var space = PdfColorSpaces.Create(dictionary[PdfShadingKeys.ColorSpace], resources, document);
        if (space == null || space is PdfPatternColorSpace)
        {
            return null;
        }

        var function = dictionary[PdfShadingKeys.Function] != null
            ? PdfFunctions.Create(dictionary[PdfShadingKeys.Function], document)
            : null;
        if (dictionary[PdfShadingKeys.Function] != null && function == null)
        {
            return null;
        }

        var background = PdfValues.Numbers(dictionary[PdfShadingKeys.Background], document);
        if (background != null && background.Length != space.Components)
        {
            background = null;
        }

        var box = PdfRectangle.From(dictionary[PdfShadingKeys.BBox], document);
        var antiAlias = PdfValues.Boolean(dictionary[PdfShadingKeys.AntiAlias], document);
        try
        {
            return type switch
            {
                1 => PdfFunctionShading.Read(dictionary, document, space, background, box, antiAlias, function),
                2 or 3 => PdfGradientShading.Read(type, dictionary, document, space, background, box, antiAlias,
                    function),
                >= 4 and <= 7 when resolved is PdfStream stream => PdfMeshShading.Read(type, stream, document, space,
                    background, box, antiAlias, function),
                _ => null,
            };
        }
        catch (PdfException)
        {
            return null;
        }
    }
}
