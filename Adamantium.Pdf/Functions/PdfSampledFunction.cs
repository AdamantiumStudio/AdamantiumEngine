using System;

namespace Adamantium.Pdf.Functions;

/// <summary>Type 0: a table of samples over the inputs, read between the nearest samples by multilinear interpolation.
/// Cubic order is read as linear.</summary>
public sealed class PdfSampledFunction : PdfFunction
{
    /// <summary>The most inputs: the interpolation visits 2^m corners.</summary>
    public const int MaxInputs = 8;

    private readonly int[] _size;
    private readonly double[] _encode;
    private readonly double[] _decode;
    private readonly float[] _samples;
    private readonly int _outputs;

    /// <param name="samples">Each sample scaled to 0 to 1, outputs innermost, the first input next.</param>
    public PdfSampledFunction(double[] domain, double[] range, int[] size, double[] encode, double[] decode,
        float[] samples) : base(domain, range)
    {
        _size = size;
        _encode = encode;
        _decode = decode;
        _samples = samples;
        _outputs = range.Length / 2;
    }

    public override int Outputs => _outputs;

    protected override void Compute(ReadOnlySpan<double> input, Span<double> output)
    {
        var m = _size.Length;
        Span<int> lower = stackalloc int[m];
        Span<double> weight = stackalloc double[m];
        for (var i = 0; i < m; i++)
        {
            var x = i < input.Length ? input[i] : 0;
            var e = Interpolate(x, Domain[2 * i], Domain[2 * i + 1], _encode[2 * i], _encode[2 * i + 1]);
            e = Clip(e, 0, _size[i] - 1);
            lower[i] = Math.Min((int)Math.Floor(e), Math.Max(0, _size[i] - 2));
            weight[i] = _size[i] == 1 ? 0 : e - lower[i];
        }

        for (var j = 0; j < output.Length && j < _outputs; j++)
        {
            double sum = 0;
            for (var corner = 0; corner < 1 << m; corner++)
            {
                double w = 1;
                var index = 0;
                var stride = 1;
                for (var i = 0; i < m; i++)
                {
                    var upper = (corner >> i & 1) == 1;
                    w *= upper ? weight[i] : 1 - weight[i];
                    var position = Math.Min(lower[i] + (upper ? 1 : 0), _size[i] - 1);
                    index += position * stride;
                    stride *= _size[i];
                }

                if (w != 0)
                {
                    sum += w * _samples[index * _outputs + j];
                }
            }

            output[j] = Interpolate(sum, 0, 1, _decode[2 * j], _decode[2 * j + 1]);
        }
    }
}
