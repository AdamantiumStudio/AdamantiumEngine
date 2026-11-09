using System;
using Adamantium.Imaging.Icc;
using Adamantium.Pdf.ColorSpaces;

namespace Adamantium.Pdf.Images;

/// <summary>A color space evaluated once at the nodes of a grid over its components and read between them by
/// multilinear interpolation: what an image in a space with a tint transform is drawn through, so a function runs a
/// bounded number of times however many pixels there are. A space shown through an ICC profile is held as linear sRGB,
/// not clipped, on a grid of about twice the nodes along each component, and clipped and encoded once interpolated: the
/// encoded colors bend sharply at the edge of the gamut and in the shadows. For up to <see cref="MaxComponents"/>
/// components.</summary>
internal sealed class PdfColorGrid
{
    public const int MaxComponents = 8;

    private const int EncodedSteps = 1 << 16;

    private static readonly float[] Encoded = EncodeTable();

    private readonly int _components;
    private readonly int _nodes;
    private readonly bool _linear;
    private readonly double[] _low;
    private readonly double[] _high;
    private readonly float[] _rgb;

    public PdfColorGrid(PdfColorSpace space, double[] decode, int components)
    {
        _components = components;
        var managed = space as PdfIccBasedColorSpace;
        _linear = managed is { IsManaged: true };
        _nodes = NodesAlong(components, _linear);
        _low = new double[components];
        _high = new double[components];
        for (var c = 0; c < components; c++)
        {
            _low[c] = Math.Min(decode[2 * c], decode[2 * c + 1]);
            _high[c] = Math.Max(decode[2 * c], decode[2 * c + 1]);
        }

        var count = NodeCount(space, components);
        _rgb = new float[3 * count];
        Span<double> color = stackalloc double[components];
        Span<double> rgb = stackalloc double[3];
        for (var node = 0; node < count; node++)
        {
            var rest = node;
            for (var c = 0; c < components; c++)
            {
                var step = rest % _nodes;
                rest /= _nodes;
                color[c] = _low[c] + (_high[c] - _low[c]) * step / (_nodes - 1);
            }

            if (_linear)
            {
                managed.ToLinearRgb(color, rgb);
            }
            else
            {
                space.ToRgb(color, rgb);
            }

            _rgb[3 * node] = (float)rgb[0];
            _rgb[3 * node + 1] = (float)rgb[1];
            _rgb[3 * node + 2] = (float)rgb[2];
        }
    }

    /// <summary>How many times a grid over <paramref name="components"/> of <paramref name="space"/> evaluates it.
    /// </summary>
    public static int NodeCount(PdfColorSpace space, int components)
    {
        var nodes = NodesAlong(components, space is PdfIccBasedColorSpace { IsManaged: true });
        var count = 1;
        for (var c = 0; c < components; c++)
        {
            count *= nodes;
        }

        return count;
    }

    private static int NodesAlong(int components, bool fine) => components switch
    {
        1 => 257,
        2 => fine ? 65 : 33,
        3 => fine ? 33 : 17,
        4 => fine ? 17 : 9,
        5 => fine ? 9 : 5,
        6 => fine ? 6 : 4,
        _ => 3,
    };

    public void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        Span<int> lower = stackalloc int[_components];
        Span<double> weight = stackalloc double[_components];
        for (var c = 0; c < _components; c++)
        {
            var span = _high[c] - _low[c];
            var position = span <= 0 ? 0 : (components[c] - _low[c]) / span * (_nodes - 1);
            position = Math.Clamp(double.IsNaN(position) ? 0 : position, 0, _nodes - 1);
            lower[c] = Math.Min((int)position, _nodes - 2);
            weight[c] = position - lower[c];
        }

        rgb[0] = rgb[1] = rgb[2] = 0;
        for (var corner = 0; corner < 1 << _components; corner++)
        {
            double w = 1;
            var index = 0;
            var stride = 1;
            for (var c = 0; c < _components; c++)
            {
                var upper = (corner >> c & 1) == 1;
                w *= upper ? weight[c] : 1 - weight[c];
                index += (lower[c] + (upper ? 1 : 0)) * stride;
                stride *= _nodes;
            }

            if (w == 0)
            {
                continue;
            }

            rgb[0] += w * _rgb[3 * index];
            rgb[1] += w * _rgb[3 * index + 1];
            rgb[2] += w * _rgb[3 * index + 2];
        }

        if (_linear)
        {
            for (var c = 0; c < 3; c++)
            {
                rgb[c] = Encoded[(int)(Math.Clamp(rgb[c], 0, 1) * (EncodedSteps - 1) + 0.5)];
            }
        }
    }

    private static float[] EncodeTable()
    {
        var table = new float[EncodedSteps];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = IccTransform.Encode((double)i / (EncodedSteps - 1));
        }

        return table;
    }
}
