using System;
using System.IO;

namespace Adamantium.Imaging.Icc;

/// <summary>A color lookup table (ICC.1, 10.12.3): a grid over the inputs, a value for every output at every node,
/// read between nodes - tetrahedrally over three inputs, tetrahedrally in the two nearest planes of the fourth over
/// four, multilinearly otherwise.</summary>
internal sealed class IccClut
{
    private const long MaxNodes = 1 << 24;

    private readonly int[] _points;
    private readonly int[] _strides;
    private readonly float[] _values;

    public IccClut(int[] points, int outputs, float[] values)
    {
        _points = points;
        Outputs = outputs;
        _values = values;
        _strides = new int[points.Length];
        var stride = outputs;
        for (var i = points.Length - 1; i >= 0; i--)
        {
            _strides[i] = stride;
            stride *= points[i];
        }
    }

    public int Inputs => _points.Length;

    public int Outputs { get; }

    /// <summary>The nodes a table of these points has, or an error when they are more than any profile needs.</summary>
    public static int NodeCount(int[] points, int outputs)
    {
        long nodes = outputs;
        foreach (var point in points)
        {
            if (point < 2 && points.Length > 0)
            {
                throw new InvalidDataException("An ICC lookup table has a dimension of fewer than two points.");
            }

            nodes *= point;
            if (nodes > MaxNodes)
            {
                throw new InvalidDataException("An ICC lookup table has more nodes than any profile needs.");
            }
        }

        return (int)nodes;
    }

    /// <summary>Reads the values of <paramref name="input"/>, each 0..1, into <paramref name="output"/>.</summary>
    public void Evaluate(ReadOnlySpan<float> input, Span<float> output)
    {
        switch (Inputs)
        {
            case 3:
                Tetrahedral(input, output, 0, 0);
                return;
            case 4:
                Planes(input, output);
                return;
            default:
                Multilinear(input, output);
                return;
        }
    }

    private void Planes(ReadOnlySpan<float> input, Span<float> output)
    {
        var (low, high, t) = Cell(input[0], 0);
        Span<float> rest = stackalloc float[3] { input[1], input[2], input[3] };
        Span<float> lower = stackalloc float[Outputs];
        Span<float> upper = stackalloc float[Outputs];
        Tetrahedral(rest, lower, 1, low * _strides[0]);
        Tetrahedral(rest, upper, 1, high * _strides[0]);
        for (var o = 0; o < Outputs; o++)
        {
            output[o] = lower[o] + (upper[o] - lower[o]) * t;
        }
    }

    private void Tetrahedral(ReadOnlySpan<float> input, Span<float> output, int first, int origin)
    {
        var (x0, x1, rx) = Cell(input[0], first);
        var (y0, y1, ry) = Cell(input[1], first + 1);
        var (z0, z1, rz) = Cell(input[2], first + 2);
        var sx = _strides[first];
        var sy = _strides[first + 1];
        var sz = _strides[first + 2];
        var c0 = origin + x0 * sx + y0 * sy + z0 * sz;
        var dx = (x1 - x0) * sx;
        var dy = (y1 - y0) * sy;
        var dz = (z1 - z0) * sz;
        int a, b;
        float ta, tb, tc;
        if (rx >= ry && ry >= rz)
        {
            (a, b, ta, tb, tc) = (dx, dx + dy, rx, ry, rz);
        }
        else if (rx >= rz && rz >= ry)
        {
            (a, b, ta, tb, tc) = (dx, dx + dz, rx, rz, ry);
        }
        else if (rz >= rx && rx >= ry)
        {
            (a, b, ta, tb, tc) = (dz, dz + dx, rz, rx, ry);
        }
        else if (ry >= rx && rx >= rz)
        {
            (a, b, ta, tb, tc) = (dy, dy + dx, ry, rx, rz);
        }
        else if (ry >= rz && rz >= rx)
        {
            (a, b, ta, tb, tc) = (dy, dy + dz, ry, rz, rx);
        }
        else
        {
            (a, b, ta, tb, tc) = (dz, dz + dy, rz, ry, rx);
        }

        var all = dx + dy + dz;
        for (var o = 0; o < Outputs; o++)
        {
            var v0 = _values[c0 + o];
            var va = _values[c0 + a + o];
            var vb = _values[c0 + b + o];
            var v1 = _values[c0 + all + o];
            output[o] = v0 + (va - v0) * ta + (vb - va) * tb + (v1 - vb) * tc;
        }
    }

    private void Multilinear(ReadOnlySpan<float> input, Span<float> output)
    {
        var inputs = Inputs;
        Span<int> low = stackalloc int[inputs];
        Span<int> step = stackalloc int[inputs];
        Span<float> fraction = stackalloc float[inputs];
        for (var i = 0; i < inputs; i++)
        {
            var (l, h, t) = Cell(input[i], i);
            low[i] = l;
            step[i] = (h - l) * _strides[i];
            fraction[i] = t;
        }

        output[..Outputs].Clear();
        for (var corner = 0; corner < 1 << inputs; corner++)
        {
            var weight = 1f;
            var at = 0;
            for (var i = 0; i < inputs; i++)
            {
                var high = (corner >> i & 1) == 1;
                weight *= high ? fraction[i] : 1 - fraction[i];
                at += low[i] * _strides[i] + (high ? step[i] : 0);
            }

            if (weight == 0)
            {
                continue;
            }

            for (var o = 0; o < Outputs; o++)
            {
                output[o] += weight * _values[at + o];
            }
        }
    }

    private (int Low, int High, float Fraction) Cell(float value, int dimension)
    {
        var last = _points[dimension] - 1;
        var at = Math.Clamp(value, 0, 1) * last;
        var low = Math.Min((int)at, last - 1);
        return (low, low + 1, at - low);
    }
}
