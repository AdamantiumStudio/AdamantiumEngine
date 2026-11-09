using System;
using System.Collections.Generic;
using Adamantium.Pdf.Functions;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>DeviceN: a tint for each of several colorants, shown through the tint transform into the alternate space.
/// </summary>
public sealed class PdfDeviceNColorSpace : PdfColorSpace
{
    private readonly PdfColorSpace _alternate;
    private readonly PdfFunction _tintTransform;

    public PdfDeviceNColorSpace(IReadOnlyList<string> colorants, PdfColorSpace alternate, PdfFunction tintTransform)
    {
        Colorants = colorants;
        _alternate = alternate;
        _tintTransform = tintTransform;
    }

    public override string Family => PdfColorSpaceFamilies.DeviceN;

    public override int Components => Colorants.Count;

    public IReadOnlyList<string> Colorants { get; }

    /// <summary>Full tints.</summary>
    public override void InitialColor(Span<double> components) => components.Fill(1);

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        if (_tintTransform == null || _alternate == null)
        {
            double total = 0;
            for (var i = 0; i < Components; i++)
            {
                total += Clamp01(At(components, i));
            }

            rgb[0] = rgb[1] = rgb[2] = 1 - Math.Min(1, total);
            return;
        }

        Span<double> alternate = stackalloc double[Math.Max(1, _alternate.Components)];
        _tintTransform.Evaluate(components, alternate);
        _alternate.ToRgb(alternate, rgb);
    }
}
