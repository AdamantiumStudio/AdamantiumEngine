using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>ICCBased: N components described by an ICC profile. Shown through its <c>/Alternate</c>, or the device space
/// of as many components, until colors are managed by profile.</summary>
public sealed class PdfIccBasedColorSpace : PdfColorSpace
{
    private readonly PdfColorSpace _alternate;
    private readonly double[] _range;

    public PdfIccBasedColorSpace(int components, PdfColorSpace alternate, double[] range)
    {
        Components = components;
        _alternate = alternate;
        _range = range != null && range.Length == 2 * components ? range : null;
    }

    public override string Family => "ICCBased";

    public override int Components { get; }

    public PdfColorSpace Alternate => _alternate;

    public override double[] DefaultDecode(int bitsPerComponent) => _range ?? base.DefaultDecode(bitsPerComponent);

    public override void InitialColor(Span<double> components) => _alternate.InitialColor(components);

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb) => _alternate.ToRgb(components, rgb);
}
