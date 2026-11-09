using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>Indexed: one component, an index into a table of colors of the base space, one byte a base component.
/// </summary>
public sealed class PdfIndexedColorSpace : PdfColorSpace
{
    private readonly PdfColorSpace _base;
    private readonly int _highest;
    private readonly byte[] _lookup;

    public PdfIndexedColorSpace(PdfColorSpace @base, int highest, byte[] lookup)
    {
        _base = @base;
        _highest = Math.Clamp(highest, 0, 255);
        _lookup = lookup;
    }

    public override string Family => PdfColorSpaceFamilies.Indexed;

    public override int Components => 1;

    public PdfColorSpace Base => _base;

    public int Highest => _highest;

    public override double[] DefaultDecode(int bitsPerComponent) => [0, (1 << Math.Clamp(bitsPerComponent, 1, 16)) - 1];

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var index = (int)Math.Clamp(Math.Round(At(components, 0)), 0, _highest);
        var count = _base.Components;
        var decode = _base.DefaultDecode(8);
        Span<double> color = stackalloc double[Math.Max(1, count)];
        for (var i = 0; i < count; i++)
        {
            var at = index * count + i;
            var value = at < _lookup.Length ? _lookup[at] / 255.0 : 0;
            color[i] = decode[2 * i] + value * (decode[2 * i + 1] - decode[2 * i]);
        }

        _base.ToRgb(color, rgb);
    }
}
