namespace Adamantium.Pulsar.Decoding;

/// <summary>The inverse MDCT of a block of n samples from n/2 lines: y(i) = sum over k of X(k) cos(2 pi / n (i + 1/2 +
/// n/4)(k + 1/2)), as a DCT-IV of M = n/2 lines by a complex FFT of n/4. The even lines and the odd ones taken from the
/// end are its values' real and imaginary parts; turned before and after, the FFT's result gives the DCT-IV's even
/// values and, from the end, its odd ones; and its halves give the block, as its symmetries say.</summary>
internal sealed class VorbisTransform
{
    private readonly int _half;
    private readonly int _quarter;
    // For j of n/4: e^(-i pi j / M), what the lines are turned by before the FFT; e^(-i pi (4j + 1) / 4M), what its
    // result is turned by after. M is n/2.
    private readonly float[] _beforeCos;
    private readonly float[] _beforeSin;
    private readonly float[] _afterCos;
    private readonly float[] _afterSin;
    private readonly float[] _fftCos;
    private readonly float[] _fftSin;
    private readonly int[] _reversed;
    private readonly float[] _real;
    private readonly float[] _imaginary;
    private readonly float[] _dct;

    public VorbisTransform(int size)
    {
        _half = size / 2;
        _quarter = size / 4;
        _beforeCos = new float[_quarter];
        _beforeSin = new float[_quarter];
        _afterCos = new float[_quarter];
        _afterSin = new float[_quarter];
        for (var j = 0; j < _quarter; j++)
        {
            var before = -Math.PI * j / _half;
            var after = -Math.PI * (4 * j + 1) / (4.0 * _half);
            _beforeCos[j] = (float)Math.Cos(before);
            _beforeSin[j] = (float)Math.Sin(before);
            _afterCos[j] = (float)Math.Cos(after);
            _afterSin[j] = (float)Math.Sin(after);
        }

        _fftCos = new float[_quarter / 2];
        _fftSin = new float[_quarter / 2];
        for (var k = 0; k < _quarter / 2; k++)
        {
            var angle = -2 * Math.PI * k / _quarter;
            _fftCos[k] = (float)Math.Cos(angle);
            _fftSin[k] = (float)Math.Sin(angle);
        }

        _reversed = new int[_quarter];
        var bits = int.Log2(_quarter);
        for (var index = 0; index < _quarter; index++)
        {
            var reversed = 0;
            for (var bit = 0; bit < bits; bit++)
            {
                reversed |= (index >> bit & 1) << (bits - 1 - bit);
            }

            _reversed[index] = reversed;
        }

        _real = new float[_quarter];
        _imaginary = new float[_quarter];
        _dct = new float[_half];
    }

    /// <summary>The block of <paramref name="lines"/>, n/2 of them, into <paramref name="samples"/>, n.</summary>
    public void Inverse(ReadOnlySpan<float> lines, Span<float> samples)
    {
        Dct4(lines);
        // y(i) is the DCT-IV at i + n/4, extended: -u(2M - 1 - m) from M to 2M, -u(m - 2M) past it.
        var half = _half;
        for (var index = 0; index < 2 * half; index++)
        {
            var at = index + half / 2;
            samples[index] = at < half ? _dct[at] : at < 2 * half ? -_dct[2 * half - 1 - at] : -_dct[at - 2 * half];
        }
    }

    /// <summary>u(m) = sum over k of X(k) cos(pi / M (m + 1/2)(k + 1/2)), for testing the turns and the FFT.</summary>
    public ReadOnlySpan<float> Dct4(ReadOnlySpan<float> lines)
    {
        var half = _half;
        for (var j = 0; j < _quarter; j++)
        {
            var real = lines[2 * j];
            var imaginary = lines[half - 1 - 2 * j];
            var at = _reversed[j];
            _real[at] = real * _beforeCos[j] - imaginary * _beforeSin[j];
            _imaginary[at] = real * _beforeSin[j] + imaginary * _beforeCos[j];
        }

        Fft();
        for (var k = 0; k < _quarter; k++)
        {
            var real = _real[k] * _afterCos[k] - _imaginary[k] * _afterSin[k];
            var imaginary = _real[k] * _afterSin[k] + _imaginary[k] * _afterCos[k];
            _dct[2 * k] = real;
            _dct[half - 1 - 2 * k] = -imaginary;
        }

        return _dct;
    }

    // In place, of values already in bit reversed order: radix 2, e^(-2 pi i jk / N).
    private void Fft()
    {
        var size = _quarter;
        for (var span = 2; span <= size; span *= 2)
        {
            var stride = size / span;
            var middle = span / 2;
            for (var start = 0; start < size; start += span)
            {
                for (var k = 0; k < middle; k++)
                {
                    var cos = _fftCos[k * stride];
                    var sin = _fftSin[k * stride];
                    var top = start + k;
                    var bottom = top + middle;
                    var real = _real[bottom] * cos - _imaginary[bottom] * sin;
                    var imaginary = _real[bottom] * sin + _imaginary[bottom] * cos;
                    _real[bottom] = _real[top] - real;
                    _imaginary[bottom] = _imaginary[top] - imaginary;
                    _real[top] += real;
                    _imaginary[top] += imaginary;
                }
            }
        }
    }
}
