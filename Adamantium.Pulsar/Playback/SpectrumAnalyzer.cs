namespace Adamantium.Pulsar.Playback;

/// <summary>The spectrum of the last frames an engine mixed, in bands spaced as pitch is heard: by a Hann window and a
/// Fourier transform of <see cref="Size"/> points.</summary>
internal sealed class SpectrumAnalyzer
{
    public const int Size = 2048;
    private const int Levels = 11;
    private const double LowestFrequency = 40;
    private const double HighestFrequency = 16000;
    // Of a band, what is shown as nothing: quieter than that, in decibels below full scale.
    private const float Floor = -72;

    private readonly float[] _window = new float[Size];
    private readonly float[] _real = new float[Size];
    private readonly float[] _imaginary = new float[Size];
    private readonly float[] _cosines = new float[Size / 2];
    private readonly float[] _sines = new float[Size / 2];
    private readonly int[] _reversed = new int[Size];
    private readonly float[] _played = new float[2 * Size];
    // Of the window's gain: a sine of full scale is 1.
    private readonly float _scale;

    public SpectrumAnalyzer()
    {
        var sum = 0.0;
        for (var n = 0; n < Size; n++)
        {
            _window[n] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (Size - 1)));
            sum += _window[n];
            for (var bit = 0; bit < Levels; bit++)
            {
                _reversed[n] |= (n >> bit & 1) << (Levels - 1 - bit);
            }
        }

        for (var k = 0; k < Size / 2; k++)
        {
            _cosines[k] = (float)Math.Cos(2 * Math.PI * k / Size);
            _sines[k] = (float)-Math.Sin(2 * Math.PI * k / Size);
        }

        _scale = (float)(2 / sum);
    }

    /// <summary>Each band 0 (<see cref="Floor"/> or quieter) to 1 (full scale), of what <paramref name="engine"/> mixed
    /// last into <paramref name="bus"/>, before its volume.</summary>
    public void Analyze(AudioEngine engine, BusId bus, Span<float> bands)
    {
        var count = engine.CopyPlayed(bus, _played);
        _played.AsSpan(2 * count).Clear();
        for (var n = 0; n < Size; n++)
        {
            _real[_reversed[n]] = (_played[2 * n] + _played[2 * n + 1]) * 0.5f * _window[n];
            _imaginary[n] = 0;
        }

        Transform();
        var rate = (double)AudioFormat.Default.SampleRate;
        var ratio = HighestFrequency / LowestFrequency;
        for (var band = 0; band < bands.Length; band++)
        {
            var from = (int)(LowestFrequency * Math.Pow(ratio, (double)band / bands.Length) * Size / rate);
            var to = (int)Math.Ceiling(LowestFrequency * Math.Pow(ratio, (band + 1.0) / bands.Length) * Size / rate);
            to = Math.Clamp(to, from + 1, Size / 2);
            var power = 0f;
            for (var bin = Math.Min(from, Size / 2 - 1); bin < to; bin++)
            {
                power = Math.Max(power, _real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]);
            }

            var decibels = 10 * MathF.Log10(power * _scale * _scale + 1e-12f);
            bands[band] = Math.Clamp(1 - decibels / Floor, 0, 1);
        }
    }

    // In place, of the samples put in bit-reversed order: radix 2, by decimation in time.
    private void Transform()
    {
        for (var half = 1; half < Size; half *= 2)
        {
            var step = Size / (2 * half);
            for (var start = 0; start < Size; start += 2 * half)
            {
                for (var k = 0; k < half; k++)
                {
                    var cosine = _cosines[k * step];
                    var sine = _sines[k * step];
                    var top = start + k;
                    var bottom = top + half;
                    var real = _real[bottom] * cosine - _imaginary[bottom] * sine;
                    var imaginary = _real[bottom] * sine + _imaginary[bottom] * cosine;
                    _real[bottom] = _real[top] - real;
                    _imaginary[bottom] = _imaginary[top] - imaginary;
                    _real[top] += real;
                    _imaginary[top] += imaginary;
                }
            }
        }
    }
}
