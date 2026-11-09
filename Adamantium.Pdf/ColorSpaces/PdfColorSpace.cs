using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>A color space (ISO 32000-2, 8.6): how many components a color has, where they start, and what they look
/// like on screen, in sRGB.</summary>
public abstract class PdfColorSpace
{
    /// <summary>The family's name: DeviceRGB, Indexed, Separation and so on.</summary>
    public abstract string Family { get; }

    public abstract int Components { get; }

    /// <summary>The color a <c>CS</c>/<c>cs</c> operator sets: zero in every component, unless the family says
    /// otherwise.</summary>
    public virtual void InitialColor(Span<double> components) => components.Clear();

    /// <summary>The bounds of each component an image's samples map onto without a <c>/Decode</c>: pairs of low and
    /// high.</summary>
    public virtual double[] DefaultDecode(int bitsPerComponent)
    {
        var decode = new double[2 * Components];
        for (var i = 0; i < Components; i++)
        {
            decode[2 * i + 1] = 1;
        }

        return decode;
    }

    /// <summary>The color in sRGB, each channel from 0 to 1.</summary>
    public abstract void ToRgb(ReadOnlySpan<double> components, Span<double> rgb);

    protected static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : double.IsNaN(value) ? 0 : value;

    protected static double At(ReadOnlySpan<double> components, int index) =>
        index < components.Length ? components[index] : 0;
}
