using System;
using Adamantium.Pdf.ColorSpaces;

namespace Adamantium.Pdf.Images;

/// <summary>A color space evaluated once at the nodes of a grid over its components and read between them by
/// multilinear interpolation: what an image in a space with a tint transform is drawn through, so a function runs a
/// bounded number of times however many pixels there are. For up to <see cref="MaxComponents"/> components.</summary>
internal sealed class PdfColorGrid
{
    public const int MaxComponents = 8;

    private readonly int _components;
    private readonly int _nodes;
    private readonly double[] _low;
    private readonly double[] _high;
    private readonly float[] _rgb;

    public PdfColorGrid(PdfColorSpace space, double[] decode, int components)
    {
        _components = components;
        _nodes = components switch
        {
            1 => 257,
            2 => 33,
            3 => 17,
            4 => 9,
            5 => 5,
            6 => 4,
            _ => 3,
        };
        _low = new double[components];
        _high = new double[components];
        for (var c = 0; c < components; c++)
        {
            _low[c] = Math.Min(decode[2 * c], decode[2 * c + 1]);
            _high[c] = Math.Max(decode[2 * c], decode[2 * c + 1]);
        }

        var count = 1;
        for (var c = 0; c < components; c++)
        {
            count *= _nodes;
        }

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

            space.ToRgb(color, rgb);
            _rgb[3 * node] = (float)rgb[0];
            _rgb[3 * node + 1] = (float)rgb[1];
            _rgb[3 * node + 2] = (float)rgb[2];
        }
    }

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
    }
}
