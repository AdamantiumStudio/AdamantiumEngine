using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf;

/// <summary>Numbers out of objects, resolved and checked: what operands and dictionary entries are read through.
/// </summary>
internal static class PdfValues
{
    public static double Number(PdfObject value, IPdfObjectResolver resolver, double fallback = 0) =>
        resolver.Resolve(value) switch
        {
            PdfInteger integer => integer.Value,
            PdfReal real when double.IsFinite(real.Value) => real.Value,
            _ => fallback,
        };

    public static int Integer(PdfObject value, IPdfObjectResolver resolver, int fallback = 0) =>
        resolver.Resolve(value) switch
        {
            PdfInteger integer when integer.Value is >= int.MinValue and <= int.MaxValue => (int)integer.Value,
            PdfReal real when double.IsFinite(real.Value) && real.Value is >= int.MinValue and <= int.MaxValue =>
                (int)real.Value,
            _ => fallback,
        };

    /// <summary>An array of numbers, or null when it is not one; entries that are not numbers read as zero.</summary>
    public static double[] Numbers(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfArray array)
        {
            return null;
        }

        var numbers = new double[array.Count];
        for (var i = 0; i < numbers.Length; i++)
        {
            numbers[i] = Number(array[i], resolver);
        }

        return numbers;
    }

    public static bool Boolean(PdfObject value, IPdfObjectResolver resolver, bool fallback = false) =>
        resolver.Resolve(value) is PdfBoolean boolean ? boolean.Value : fallback;

    public static string Name(PdfObject value, IPdfObjectResolver resolver) =>
        (resolver.Resolve(value) as PdfName)?.Value;
}
