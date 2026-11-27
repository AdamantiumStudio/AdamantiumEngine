namespace Adamantium.Pulsar.Decoding;

/// <summary>CELT's inverse MDCT (RFC 6716, 4.3.7): of n/2 lines, by a complex FFT of n/4 turned before and after, into
/// n/2 + 120 samples whose first and last 120 are windowed by the low overlap window, the first added to what is
/// there. For blocks of 120 to 960 lines; their FFTs, of 60 to 480, are of mixed radices.</summary>
internal sealed class CeltTransform
{
    private const int Longest = 1920;

    // cos(2 pi i / 1920), for i up to 1920 / 4.
    private readonly float[] _turns = [.. Enumerable.Range(0, Longest / 4 + 1).Select(index => (float)Math.Cos(2 * Math.PI * index / Longest))];
    private readonly CeltFft[] _ffts = [new(480), new(240), new(120), new(60)];
    private readonly float[] _spectrum = new float[Longest / 2];
    private readonly float[] _turned = new float[Longest / 2];

    /// <summary>The block of <paramref name="lines"/>' lines, every <paramref name="stride"/>th from the first, into
    /// <paramref name="output"/>, 120 samples before where the block's first is.</summary>
    /// <param name="lines">The spectral lines.</param>
    /// <param name="stride">How far apart the block's lines are.</param>
    /// <param name="output">Where the samples go.</param>
    /// <param name="shift">0 for 960 lines, 3 for 120.</param>
    public void Inverse(ReadOnlySpan<float> lines, int stride, Span<float> output, int shift)
    {
        var size = Longest >> shift;
        var half = size >> 1;
        var quarter = size >> 2;
        var overlap = CeltTables.Overlap;
        var sine = (float)(2 * Math.PI * .125 / size);
        var turns = _turns;
        var turned = _turned.AsSpan(0, half);
        var spectrum = _spectrum.AsSpan(0, half);
        for (var at = 0; at < quarter; at++)
        {
            var first = lines[2 * stride * at];
            var last = lines[stride * (half - 1) - 2 * stride * at];
            var cos = turns[at << shift];
            var sin = turns[(quarter - at) << shift];
            var real = -last * cos + first * sin;
            var imaginary = -last * sin - first * cos;
            turned[2 * at] = real - imaginary * sine;
            turned[2 * at + 1] = imaginary + real * sine;
        }

        _ffts[shift].Inverse(turned, spectrum);
        for (var at = 0; at < quarter; at++)
        {
            var real = spectrum[2 * at];
            var imaginary = spectrum[2 * at + 1];
            var cos = turns[at << shift];
            var sin = turns[(quarter - at) << shift];
            var turnedReal = real * cos - imaginary * sin;
            var turnedImaginary = imaginary * cos + real * sin;
            spectrum[2 * at] = turnedReal - turnedImaginary * sine;
            spectrum[2 * at + 1] = turnedImaginary + turnedReal * sine;
        }

        for (var at = 0; at < quarter; at++)
        {
            turned[2 * at] = -spectrum[2 * at];
            turned[2 * at + 1] = spectrum[half - 1 - 2 * at];
        }

        // Of output, from where the window's nonzero part begins: (half - overlap) / 2 before its start.
        var origin = -((half - overlap) >> 1);
        var window = CeltTables.Window;
        var from = quarter - 1;
        var high = origin + half - 1;
        var low = origin + quarter - overlap / 2;
        var index = 0;
        for (; index < quarter - overlap / 2; index++)
        {
            output[high--] = turned[from--];
        }

        for (var w = 0; index < quarter; index++, w++)
        {
            var value = turned[from--];
            output[low++] += -window[w] * value;
            output[high--] += window[overlap - 1 - w] * value;
        }

        from = quarter;
        high = origin + half;
        low = origin + size - 1 - (quarter - overlap / 2);
        index = 0;
        for (; index < quarter - overlap / 2; index++)
        {
            output[high++] = turned[from++];
        }

        for (var w = 0; index < quarter; index++, w++)
        {
            var value = turned[from++];
            output[low--] = window[w] * value;
            output[high++] = window[overlap - 1 - w] * value;
        }
    }
}
