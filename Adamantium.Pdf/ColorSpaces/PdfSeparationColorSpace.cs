using System;
using Adamantium.Pdf.Functions;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>Separation: one colorant's tint from 0 to 1, shown through the tint transform into the alternate space.
/// <c>/All</c> is every colorant at once, <c>/None</c> paints nothing.</summary>
public sealed class PdfSeparationColorSpace : PdfColorSpace
{
    private readonly PdfColorSpace _alternate;
    private readonly PdfFunction _tintTransform;

    public PdfSeparationColorSpace(string colorant, PdfColorSpace alternate, PdfFunction tintTransform)
    {
        Colorant = colorant;
        _alternate = alternate;
        _tintTransform = tintTransform;
    }

    public override string Family => "Separation";

    public override int Components => 1;

    public string Colorant { get; }

    /// <summary>Whether marks in this space are not painted at all.</summary>
    public bool PaintsNothing => Colorant == "None";

    /// <summary>A full tint.</summary>
    public override void InitialColor(Span<double> components) => components[0] = 1;

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var tint = Clamp01(At(components, 0));
        if (Colorant == "All" || _tintTransform == null || _alternate == null)
        {
            rgb[0] = rgb[1] = rgb[2] = 1 - tint;
            return;
        }

        Span<double> alternate = stackalloc double[Math.Max(1, _alternate.Components)];
        _tintTransform.Evaluate([tint], alternate);
        _alternate.ToRgb(alternate, rgb);
    }
}
