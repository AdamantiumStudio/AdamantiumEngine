using System;

namespace Adamantium.Imaging.Icc;

/// <summary>Converts many device colors to sRGB through an <see cref="IccTransform"/>. For more colors than a grid over
/// the device values has nodes, the transform is evaluated once at each node as linear sRGB, not clipped, read between
/// nodes multilinearly, then clipped and encoded - encoded colors bend sharply at the edge of the gamut and in the
/// shadows, linear ones do not; for fewer colors, and for more than four channels, each goes through the transform.
/// Safe to use from several threads at once.</summary>
public sealed class IccPixelConverter
{
    private const int EncodedSteps = 1 << 16;

    private static readonly Lazy<float[]> Encoded = new(EncodeTable);

    private readonly IccTransform _transform;
    private readonly int _nodes;
    private readonly float[] _grid;

    /// <summary>A converter for about <paramref name="colors"/> colors through <paramref name="transform"/>.</summary>
    public IccPixelConverter(IccTransform transform, long colors)
    {
        ArgumentNullException.ThrowIfNull(transform);
        _transform = transform;
        Channels = transform.Components;
        _nodes = Channels switch
        {
            1 => 257,
            2 => 65,
            3 => 33,
            4 => 17,
            _ => 0,
        };
        if (_nodes == 0 || colors <= NodeCount(Channels))
        {
            return;
        }

        var count = NodeCount(Channels);
        _grid = new float[3 * count];
        Span<float> device = stackalloc float[Channels];
        Span<float> linear = stackalloc float[3];
        for (var node = 0; node < count; node++)
        {
            var rest = node;
            for (var c = 0; c < Channels; c++)
            {
                device[c] = (float)(rest % _nodes) / (_nodes - 1);
                rest /= _nodes;
            }

            transform.ConvertToLinear(device, linear);
            linear.CopyTo(_grid.AsSpan(3 * node));
        }
    }

    /// <summary>The device channels a color has.</summary>
    public int Channels { get; }

    /// <summary>How many colors the transform is evaluated for when a grid is made over <paramref name="channels"/>;
    /// a converter for fewer colors evaluates each.</summary>
    public static int NodeCount(int channels) => channels switch
    {
        1 => 257,
        2 => 65 * 65,
        3 => 33 * 33 * 33,
        4 => 17 * 17 * 17 * 17,
        _ => int.MaxValue,
    };

    /// <summary>Converts device values, each 0..1, to sRGB, each 0..1.</summary>
    public void Convert(ReadOnlySpan<float> device, Span<float> rgb)
    {
        if (_grid == null)
        {
            _transform.Convert(device, rgb);
            return;
        }

        Span<int> lower = stackalloc int[Channels];
        Span<float> weight = stackalloc float[Channels];
        for (var c = 0; c < Channels; c++)
        {
            var position = Math.Clamp(float.IsNaN(device[c]) ? 0 : device[c], 0, 1) * (_nodes - 1);
            lower[c] = Math.Min((int)position, _nodes - 2);
            weight[c] = position - lower[c];
        }

        float r = 0, g = 0, b = 0;
        for (var corner = 0; corner < 1 << Channels; corner++)
        {
            var w = 1f;
            var index = 0;
            var stride = 1;
            for (var c = 0; c < Channels; c++)
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

            r += w * _grid[3 * index];
            g += w * _grid[3 * index + 1];
            b += w * _grid[3 * index + 2];
        }

        var encoded = Encoded.Value;
        rgb[0] = encoded[(int)(Math.Clamp(r, 0, 1) * (EncodedSteps - 1) + 0.5f)];
        rgb[1] = encoded[(int)(Math.Clamp(g, 0, 1) * (EncodedSteps - 1) + 0.5f)];
        rgb[2] = encoded[(int)(Math.Clamp(b, 0, 1) * (EncodedSteps - 1) + 0.5f)];
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
