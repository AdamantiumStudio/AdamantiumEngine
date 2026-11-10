using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Graphics;

/// <summary>A line dash pattern (ISO 32000-2, 8.4.3.6): the lengths of dashes and gaps in turn, and how far into them a
/// line starts. No lengths, or all of them zero, is a solid line.</summary>
public readonly record struct PdfDashPattern(double[] Lengths, double Phase)
{
    public static PdfDashPattern Solid { get; } = new([], 0);

    /// <summary>The pattern of a dash array and phase as <c>d</c> and <c>/D</c> write them; solid when the array is
    /// not one, or holds a length that is negative or not a number.</summary>
    public static PdfDashPattern From(PdfObject lengths, double phase, IPdfObjectResolver resolver)
    {
        var values = PdfValues.Numbers(lengths, resolver);
        if (values == null || resolver.Resolve(lengths) is not PdfArray array)
        {
            return Solid;
        }

        for (var i = 0; i < values.Length; i++)
        {
            if (resolver.Resolve(array[i]) is not (PdfInteger or PdfReal) || values[i] < 0)
            {
                return Solid;
            }
        }

        return new PdfDashPattern(values, double.IsFinite(phase) ? phase : 0);
    }

    public bool IsSolid
    {
        get
        {
            if (Lengths == null)
            {
                return true;
            }

            foreach (var length in Lengths)
            {
                if (length > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
