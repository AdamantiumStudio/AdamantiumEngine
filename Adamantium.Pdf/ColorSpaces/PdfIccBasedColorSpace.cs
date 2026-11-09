using System;
using Adamantium.Imaging.Icc;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>ICCBased: N components described by an ICC profile, shown through the profile as sRGB - or through its
/// <c>/Alternate</c>, or the device space of as many components, when the profile cannot be read or does not fit. A
/// <c>/Range</c> with a component whose high end is not above its low is left out, as 0 to 1.</summary>
public sealed class PdfIccBasedColorSpace : PdfColorSpace
{
    private readonly PdfColorSpace _alternate;
    private readonly double[] _range;
    private readonly IccTransform _transform;

    public PdfIccBasedColorSpace(int components, PdfColorSpace alternate, double[] range)
        : this(components, alternate, range, null)
    {
    }

    /// <summary>A space shown through <paramref name="transform"/>, or its alternate when that is null.</summary>
    public PdfIccBasedColorSpace(int components, PdfColorSpace alternate, double[] range, IccTransform transform)
    {
        Components = components;
        _alternate = alternate;
        _range = IsRange(range, components) ? range : null;
        _transform = transform != null && transform.Components == components ? transform : null;
    }

    public override string Family => PdfColorSpaceFamilies.ICCBased;

    public override int Components { get; }

    public PdfColorSpace Alternate => _alternate;

    /// <summary>Whether colors go through the profile, not the alternate.</summary>
    public bool IsManaged => _transform != null;

    public override double[] DefaultDecode(int bitsPerComponent) => _range ?? base.DefaultDecode(bitsPerComponent);

    public override void InitialColor(Span<double> components) => _alternate.InitialColor(components);

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        if (_transform == null)
        {
            _alternate.ToRgb(components, rgb);
            return;
        }

        Span<float> device = stackalloc float[Components];
        Normalize(components, device);
        Span<float> srgb = stackalloc float[3];
        _transform.Convert(device, srgb);
        rgb[0] = srgb[0];
        rgb[1] = srgb[1];
        rgb[2] = srgb[2];
    }

    /// <summary>The color as linear sRGB, not clipped (see <see cref="IccTransform.ConvertToLinear"/>), when
    /// <see cref="IsManaged"/>.</summary>
    internal void ToLinearRgb(ReadOnlySpan<double> components, Span<double> linear)
    {
        Span<float> device = stackalloc float[Components];
        Normalize(components, device);
        Span<float> values = stackalloc float[3];
        _transform.ConvertToLinear(device, values);
        linear[0] = values[0];
        linear[1] = values[1];
        linear[2] = values[2];
    }

    private static bool IsRange(double[] range, int components)
    {
        if (range == null || range.Length != 2 * components)
        {
            return false;
        }

        for (var i = 0; i < components; i++)
        {
            if (!(range[2 * i + 1] > range[2 * i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Normalize(ReadOnlySpan<double> components, Span<float> device)
    {
        for (var i = 0; i < Components; i++)
        {
            var (low, high) = _range != null ? (_range[2 * i], _range[2 * i + 1]) : (0.0, 1.0);
            device[i] = (float)((components[i] - low) / (high - low));
        }
    }
}
