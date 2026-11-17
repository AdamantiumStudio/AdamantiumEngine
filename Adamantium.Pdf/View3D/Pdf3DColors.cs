using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.View3D;

/// <summary>Colors as 3D views write them: <c>[/DeviceRGB r g b]</c> in a render mode, <c>[r g b]</c> beside a color
/// space named apart in a background.</summary>
internal static class Pdf3DColors
{
    /// <summary>The color of <paramref name="value"/>, with or without the color space before it; null for a name
    /// (a mode's <c>/BG</c>) or anything else.</summary>
    internal static Pdf3DColor From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfArray array)
        {
            return null;
        }

        var numbers = PdfValues.Numbers(array.Count == 4 ? new PdfArray([array[1], array[2], array[3]]) : array,
            resolver);
        return numbers is { Length: 3 } ? new Pdf3DColor(numbers[0], numbers[1], numbers[2]) : null;
    }
}
