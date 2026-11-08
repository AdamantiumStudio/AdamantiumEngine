using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>Pattern: color comes from a pattern named alongside; an uncolored tiling pattern takes its color in the
/// underlying space.</summary>
public sealed class PdfPatternColorSpace : PdfColorSpace
{
    public PdfPatternColorSpace(PdfColorSpace underlying)
    {
        Underlying = underlying;
    }

    public override string Family => "Pattern";

    /// <summary>The underlying space's components; a pattern name is an operand of its own.</summary>
    public override int Components => Underlying?.Components ?? 0;

    public PdfColorSpace Underlying { get; }

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        if (Underlying != null)
        {
            Underlying.ToRgb(components, rgb);
            return;
        }

        rgb[0] = rgb[1] = rgb[2] = 0;
    }
}
