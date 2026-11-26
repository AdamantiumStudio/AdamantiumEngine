namespace Adamantium.Pulsar.Decoding;

/// <summary>Floor type 0 (Vorbis I, 6): the frequency response of an LSP filter, on a Bark scale mapped to the linear one.
/// No encoder after Xiph's beta 4 writes it.</summary>
internal sealed class VorbisFloor0 : VorbisFloor
{
    private readonly int _order;
    private readonly int _rate;
    private readonly int _barkMapSize;
    private readonly int _amplitudeBits;
    private readonly int _amplitudeOffset;
    private readonly int[] _books;
    private readonly int[] _amplitudes;
    private readonly float[][] _coefficients;
    // By the length of a spectrum, its map (6.2.3); made when first wanted, for each of the two block sizes.
    private readonly Dictionary<int, int[]> _maps = [];

    public VorbisFloor0(ref VorbisBits bits, VorbisCodebook[] books, int channels)
    {
        _order = (int)bits.Read(8);
        _rate = (int)bits.Read(16);
        _barkMapSize = (int)bits.Read(16);
        _amplitudeBits = (int)bits.Read(6);
        _amplitudeOffset = (int)bits.Read(8);
        _books = new int[bits.Read(4) + 1];
        for (var index = 0; index < _books.Length; index++)
        {
            _books[index] = CheckBook((int)bits.Read(8), books);
            if (!books[_books[index]].HasValues)
            {
                throw Damaged("a floor 0 names a codebook of no vectors");
            }
        }

        if (bits.IsEnded || _order == 0 || _barkMapSize == 0 || _rate == 0)
        {
            throw Damaged("a floor 0 is of no order, rate or Bark map");
        }

        _amplitudes = new int[channels];
        // The coefficients read may be more than the order, up to a vector of the largest book's dimensions more.
        var most = _order + _books.Max(book => books[book].Dimensions);
        _coefficients = [.. Enumerable.Range(0, channels).Select(_ => new float[most])];
    }

    // 6.2.2.
    public override bool Read(ref VorbisBits bits, VorbisCodebook[] books, int channel)
    {
        var amplitude = (int)bits.Read(_amplitudeBits);
        if (amplitude == 0 || bits.IsEnded)
        {
            return false;
        }

        var bookNumber = (int)bits.Read(VorbisCodebook.ILog(_books.Length));
        if (bookNumber >= _books.Length)
        {
            return false;
        }

        var book = books[_books[bookNumber]];
        var coefficients = _coefficients[channel];
        var count = 0;
        var last = 0f;
        while (count < _order)
        {
            // Each vector after the last value of the one before: 6.2.2's "continue at step 6" would set it to zero
            // again, and leave steps 8 and 9 doing nothing.
            var vector = book.ReadVector(ref bits);
            if (vector.IsEmpty)
            {
                return false;
            }

            foreach (var value in vector)
            {
                coefficients[count++] = value + last;
            }

            last = coefficients[count - 1];
        }

        _amplitudes[channel] = amplitude;
        return true;
    }

    // 6.2.3: each run of samples of one map value one value of the curve.
    public override void Apply(int channel, Span<float> spectrum)
    {
        var map = MapOf(spectrum.Length);
        var coefficients = _coefficients[channel];
        var amplitude = _amplitudes[channel];
        var index = 0;
        while (index < spectrum.Length)
        {
            var omega = Math.PI * map[index] / _barkMapSize;
            var cosine = Math.Cos(omega);
            double p;
            double q;
            if ((_order & 1) != 0)
            {
                p = 1 - cosine * cosine;
                q = 0.25;
                for (var j = 0; j <= (_order - 3) / 2; j++)
                {
                    p *= 4 * Square(Math.Cos(coefficients[2 * j + 1]) - cosine);
                }

                for (var j = 0; j <= (_order - 1) / 2; j++)
                {
                    q *= 4 * Square(Math.Cos(coefficients[2 * j]) - cosine);
                }
            }
            else
            {
                p = (1 - cosine) / 2;
                q = (1 + cosine) / 2;
                for (var j = 0; j <= (_order - 2) / 2; j++)
                {
                    p *= 4 * Square(Math.Cos(coefficients[2 * j + 1]) - cosine);
                    q *= 4 * Square(Math.Cos(coefficients[2 * j]) - cosine);
                }
            }

            var value = (float)Math.Exp(0.11512925 * (amplitude * _amplitudeOffset /
                (((1L << _amplitudeBits) - 1) * Math.Sqrt(p + q)) - _amplitudeOffset));
            var run = map[index];
            do
            {
                spectrum[index++] *= value;
            }
            while (index < spectrum.Length && map[index] == run);
        }

        static double Square(double value) => value * value;
    }

    private int[] MapOf(int length)
    {
        if (_maps.TryGetValue(length, out var map))
        {
            return map;
        }

        map = new int[length];
        var scale = _barkMapSize / Bark(0.5 * _rate);
        for (var index = 0; index < length; index++)
        {
            map[index] = (int)Math.Min(_barkMapSize - 1, Math.Floor(Bark(_rate * index / (2.0 * length)) * scale));
        }

        _maps[length] = map;
        return map;
    }

    private static double Bark(double x) =>
        13.1 * Math.Atan(0.00074 * x) + 2.24 * Math.Atan(0.0000000185 * x * x) + 0.0001 * x;
}
