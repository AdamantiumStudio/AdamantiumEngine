using System;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A tag tree (ITU-T T.800, B.10.2): a value for each leaf of a grid, coded from the root down as how far
/// above its parent's value each node is.</summary>
internal sealed class J2kTagTree
{
    private const int Unknown = int.MaxValue;

    private readonly int[] _value;
    private readonly int[] _low;
    private readonly int[] _parent;
    private readonly int[] _path = new int[32];

    public J2kTagTree(int width, int height)
    {
        var count = 0;
        var levels = 0;
        for (int w = width, h = height; ; w = (w + 1) / 2, h = (h + 1) / 2)
        {
            count += w * h;
            levels++;
            if (w * h <= 1)
            {
                break;
            }
        }

        _value = new int[count];
        _low = new int[count];
        _parent = new int[count];
        Array.Fill(_value, Unknown);
        var start = 0;
        for (int w = width, h = height, level = 0; level < levels; level++)
        {
            var parentWidth = (w + 1) / 2;
            var parentStart = start + w * h;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    _parent[start + y * w + x] = level == levels - 1 ? -1 : parentStart + (y / 2) * parentWidth + x / 2;
                }
            }

            start = parentStart;
            w = parentWidth;
            h = (h + 1) / 2;
        }

        Width = width;
    }

    public int Width { get; }

    /// <summary>Decodes the leaf at <paramref name="leaf"/> until its value is known or known to be at least
    /// <paramref name="threshold"/>; returns whether it is below the threshold.</summary>
    public bool Decode(J2kHeaderBits bits, int leaf, int threshold)
    {
        var depth = 0;
        for (var node = leaf; node >= 0; node = _parent[node])
        {
            _path[depth++] = node;
        }

        var low = 0;
        for (var i = depth - 1; i >= 0; i--)
        {
            var node = _path[i];
            if (_low[node] < low)
            {
                _low[node] = low;
            }

            while (_value[node] == Unknown && _low[node] < threshold)
            {
                if (bits.ReadBit() == 1)
                {
                    _value[node] = _low[node];
                }
                else
                {
                    _low[node]++;
                }
            }

            low = _value[node] == Unknown ? _low[node] : _value[node];
        }

        return _value[leaf] < threshold;
    }

    /// <summary>The value of a leaf decoded to the end; a value past any bit-plane count is an error of the data.
    /// </summary>
    public int Value(J2kHeaderBits bits, int leaf)
    {
        if (!Decode(bits, leaf, MaxValue))
        {
            throw new System.IO.InvalidDataException("A JPEG 2000 tag tree has a value no code-block can have.");
        }

        return _value[leaf];
    }

    private const int MaxValue = 128;
}
