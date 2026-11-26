namespace Adamantium.Pulsar.Decoding;

/// <summary>CELT's inverse MDCT (RFC 6716, 4.3.7): of n/2 lines, by a complex FFT of n/4 turned before and after, into
/// n/2 + 120 samples whose first and last 120 are windowed by the low overlap window, the first added to what is
/// there. For blocks of 120 to 960 lines; their FFTs, of 60 to 480, are of mixed radices.</summary>
internal sealed class CeltTransform
{
    private const int Longest = 1920;

    // cos(2 pi i / 1920), for i up to 1920 / 4.
    private readonly float[] _turns = [.. Enumerable.Range(0, Longest / 4 + 1).Select(index => (float)Math.Cos(2 * Math.PI * index / Longest))];
    private readonly Fft[] _ffts = [new(480), new(240), new(120), new(60)];
    private readonly float[] _spectrum = new float[Longest / 2];
    private readonly float[] _turned = new float[Longest / 2];

    /// <summary>The block of <paramref name="lines"/>' lines, every <paramref name="stride"/>th from the first, into
    /// <paramref name="output"/>, 120 samples before where the block's first is.</summary>
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

    // An unscaled inverse FFT, x(k) = sum of X(j) e^(2 pi i jk / n), by Stockham's stages of radices 4, 2, 3 and 5.
    private sealed class Fft
    {
        private readonly int _size;
        private readonly int[] _radices;
        private readonly float[][] _turns;
        private readonly float[] _work;

        public Fft(int size)
        {
            _size = size;
            var radices = new List<int>();
            var left = size;
            foreach (var radix in (ReadOnlySpan<int>)[4, 2, 3, 5])
            {
                while (left % radix == 0)
                {
                    radices.Add(radix);
                    left /= radix;
                }
            }

            _radices = [.. radices];
            // By stage, by block k and input r past the first: e^(2 pi i r k step / n), real and imaginary.
            _turns = new float[_radices.Length][];
            var done = 1;
            for (var stage = 0; stage < _radices.Length; stage++)
            {
                var radix = _radices[stage];
                var step = size / (done * radix);
                var turns = new float[2 * done * (radix - 1)];
                for (var k = 0; k < done; k++)
                {
                    for (var r = 1; r < radix; r++)
                    {
                        var angle = 2 * Math.PI * r * k * step / size;
                        turns[2 * (k * (radix - 1) + r - 1)] = (float)Math.Cos(angle);
                        turns[2 * (k * (radix - 1) + r - 1) + 1] = (float)Math.Sin(angle);
                    }
                }

                _turns[stage] = turns;
                done *= radix;
            }

            _work = new float[2 * size];
        }

        /// <summary>Of <paramref name="input"/>'s complex values, real and imaginary side by side, into
        /// <paramref name="output"/>.</summary>
        public void Inverse(Span<float> input, Span<float> output)
        {
            var size = _size;
            // Stages alternate between the two; the last must end in output.
            var source = (_radices.Length & 1) == 0 ? output : _work.AsSpan(0, 2 * size);
            input[..(2 * size)].CopyTo(source);
            var target = (_radices.Length & 1) == 0 ? _work.AsSpan(0, 2 * size) : output;
            var done = 1;
            for (var stage = 0; stage < _radices.Length; stage++)
            {
                var radix = _radices[stage];
                var count = size / radix;
                var span = done * radix;
                var blocks = size / span;
                var turns = _turns[stage];
                switch (radix)
                {
                    case 4:
                        Radix4(source, target, turns, done, count, blocks);
                        break;
                    case 2:
                        Radix2(source, target, turns, done, count, blocks);
                        break;
                    case 3:
                        Radix3(source, target, turns, done, count, blocks);
                        break;
                    default:
                        Radix5(source, target, turns, done, count, blocks);
                        break;
                }

                var swap = source;
                source = target;
                target = swap;
                done = span;
            }
        }

        // Of input j + r count, turned by its block's turn, into output q span + k + s done: j is q done + k.
        private static void Radix2(Span<float> source, Span<float> target, float[] turns, int done, int count, int blocks)
        {
            for (var k = 0; k < done; k++)
            {
                var cos = turns[2 * k];
                var sin = turns[2 * k + 1];
                for (var q = 0; q < blocks; q++)
                {
                    var j = q * done + k;
                    var a = 2 * j;
                    var b = 2 * (j + count);
                    var br = source[b] * cos - source[b + 1] * sin;
                    var bi = source[b] * sin + source[b + 1] * cos;
                    var to = 2 * (q * 2 * done + k);
                    target[to] = source[a] + br;
                    target[to + 1] = source[a + 1] + bi;
                    target[to + 2 * done] = source[a] - br;
                    target[to + 2 * done + 1] = source[a + 1] - bi;
                }
            }
        }

        private static void Radix4(Span<float> source, Span<float> target, float[] turns, int done, int count, int blocks)
        {
            for (var k = 0; k < done; k++)
            {
                var c1 = turns[6 * k];
                var s1 = turns[6 * k + 1];
                var c2 = turns[6 * k + 2];
                var s2 = turns[6 * k + 3];
                var c3 = turns[6 * k + 4];
                var s3 = turns[6 * k + 5];
                for (var q = 0; q < blocks; q++)
                {
                    var j = q * done + k;
                    var i0 = 2 * j;
                    var i1 = 2 * (j + count);
                    var i2 = 2 * (j + 2 * count);
                    var i3 = 2 * (j + 3 * count);
                    var a0r = source[i0];
                    var a0i = source[i0 + 1];
                    var a1r = source[i1] * c1 - source[i1 + 1] * s1;
                    var a1i = source[i1] * s1 + source[i1 + 1] * c1;
                    var a2r = source[i2] * c2 - source[i2 + 1] * s2;
                    var a2i = source[i2] * s2 + source[i2 + 1] * c2;
                    var a3r = source[i3] * c3 - source[i3 + 1] * s3;
                    var a3i = source[i3] * s3 + source[i3 + 1] * c3;
                    var t0r = a0r + a2r;
                    var t0i = a0i + a2i;
                    var t1r = a0r - a2r;
                    var t1i = a0i - a2i;
                    var t2r = a1r + a3r;
                    var t2i = a1i + a3i;
                    // i (a1 - a3).
                    var t3r = -(a1i - a3i);
                    var t3i = a1r - a3r;
                    var to = 2 * (q * 4 * done + k);
                    var stride = 2 * done;
                    target[to] = t0r + t2r;
                    target[to + 1] = t0i + t2i;
                    target[to + stride] = t1r + t3r;
                    target[to + stride + 1] = t1i + t3i;
                    target[to + 2 * stride] = t0r - t2r;
                    target[to + 2 * stride + 1] = t0i - t2i;
                    target[to + 3 * stride] = t1r - t3r;
                    target[to + 3 * stride + 1] = t1i - t3i;
                }
            }
        }

        private static void Radix3(Span<float> source, Span<float> target, float[] turns, int done, int count, int blocks)
        {
            const float half = -.5f;
            const float sine = .8660254f;
            for (var k = 0; k < done; k++)
            {
                var c1 = turns[4 * k];
                var s1 = turns[4 * k + 1];
                var c2 = turns[4 * k + 2];
                var s2 = turns[4 * k + 3];
                for (var q = 0; q < blocks; q++)
                {
                    var j = q * done + k;
                    var i0 = 2 * j;
                    var i1 = 2 * (j + count);
                    var i2 = 2 * (j + 2 * count);
                    var a1r = source[i1] * c1 - source[i1 + 1] * s1;
                    var a1i = source[i1] * s1 + source[i1 + 1] * c1;
                    var a2r = source[i2] * c2 - source[i2 + 1] * s2;
                    var a2i = source[i2] * s2 + source[i2 + 1] * c2;
                    var sr = a1r + a2r;
                    var si = a1i + a2i;
                    var dr = a1r - a2r;
                    var di = a1i - a2i;
                    var mr = source[i0] + half * sr;
                    var mi = source[i0 + 1] + half * si;
                    var to = 2 * (q * 3 * done + k);
                    var stride = 2 * done;
                    target[to] = source[i0] + sr;
                    target[to + 1] = source[i0 + 1] + si;
                    target[to + stride] = mr - sine * di;
                    target[to + stride + 1] = mi + sine * dr;
                    target[to + 2 * stride] = mr + sine * di;
                    target[to + 2 * stride + 1] = mi - sine * dr;
                }
            }
        }

        private static void Radix5(Span<float> source, Span<float> target, float[] turns, int done, int count, int blocks)
        {
            var cos1 = (float)Math.Cos(2 * Math.PI / 5);
            var cos2 = (float)Math.Cos(4 * Math.PI / 5);
            var sin1 = (float)Math.Sin(2 * Math.PI / 5);
            var sin2 = (float)Math.Sin(4 * Math.PI / 5);
            Span<float> real = stackalloc float[5];
            Span<float> imaginary = stackalloc float[5];
            for (var k = 0; k < done; k++)
            {
                for (var q = 0; q < blocks; q++)
                {
                    var j = q * done + k;
                    real[0] = source[2 * j];
                    imaginary[0] = source[2 * j + 1];
                    for (var r = 1; r < 5; r++)
                    {
                        var at = 2 * (j + r * count);
                        var cos = turns[2 * (k * 4 + r - 1)];
                        var sin = turns[2 * (k * 4 + r - 1) + 1];
                        real[r] = source[at] * cos - source[at + 1] * sin;
                        imaginary[r] = source[at] * sin + source[at + 1] * cos;
                    }

                    var s14r = real[1] + real[4];
                    var s14i = imaginary[1] + imaginary[4];
                    var d14r = real[1] - real[4];
                    var d14i = imaginary[1] - imaginary[4];
                    var s23r = real[2] + real[3];
                    var s23i = imaginary[2] + imaginary[3];
                    var d23r = real[2] - real[3];
                    var d23i = imaginary[2] - imaginary[3];
                    var m1r = real[0] + cos1 * s14r + cos2 * s23r;
                    var m1i = imaginary[0] + cos1 * s14i + cos2 * s23i;
                    var m2r = real[0] + cos2 * s14r + cos1 * s23r;
                    var m2i = imaginary[0] + cos2 * s14i + cos1 * s23i;
                    // i (sin1 d14 + sin2 d23) and i (sin2 d14 - sin1 d23).
                    var n1r = -(sin1 * d14i + sin2 * d23i);
                    var n1i = sin1 * d14r + sin2 * d23r;
                    var n2r = -(sin2 * d14i - sin1 * d23i);
                    var n2i = sin2 * d14r - sin1 * d23r;
                    var to = 2 * (q * 5 * done + k);
                    var stride = 2 * done;
                    target[to] = real[0] + s14r + s23r;
                    target[to + 1] = imaginary[0] + s14i + s23i;
                    target[to + stride] = m1r + n1r;
                    target[to + stride + 1] = m1i + n1i;
                    target[to + 2 * stride] = m2r + n2r;
                    target[to + 2 * stride + 1] = m2i + n2i;
                    target[to + 3 * stride] = m2r - n2r;
                    target[to + 3 * stride + 1] = m2i - n2i;
                    target[to + 4 * stride] = m1r - n1r;
                    target[to + 4 * stride + 1] = m1i - n1i;
                }
            }
        }
    }
}
