namespace Adamantium.Pulsar.Decoding;

/// <summary>Floor type 1 (Vorbis I, 7): lines through points of a list of X values, their Y values read as differences
/// from the line through the points before, on a dB scale made linear by table 10.1.</summary>
internal sealed class VorbisFloor1 : VorbisFloor
{
    private const int MostValues = 65;
    private static readonly int[] Ranges = [256, 128, 86, 64];

    private readonly int[] _partitionClasses;
    private readonly int[] _classDimensions;
    private readonly int[] _classSubclasses;
    private readonly int[] _classMasterbooks;
    // By class and subclass: class * 8 + subclass; -1 for none.
    private readonly int[] _subclassBooks;
    private readonly int _multiplier;
    private readonly int[] _x;
    private readonly int[] _sorted;
    private readonly int[] _low;
    private readonly int[] _high;
    // By channel, the values read, then the final ones and which are drawn to.
    private readonly int[][] _y;
    private readonly int[] _finalY;
    private readonly bool[] _drawn;

    public VorbisFloor1(ref VorbisBits bits, VorbisCodebook[] books, int channels)
    {
        var partitions = (int)bits.Read(5);
        _partitionClasses = new int[partitions];
        var maximumClass = -1;
        for (var partition = 0; partition < partitions; partition++)
        {
            _partitionClasses[partition] = (int)bits.Read(4);
            maximumClass = Math.Max(maximumClass, _partitionClasses[partition]);
        }

        _classDimensions = new int[maximumClass + 1];
        _classSubclasses = new int[maximumClass + 1];
        _classMasterbooks = new int[maximumClass + 1];
        _subclassBooks = new int[(maximumClass + 1) * 8];
        for (var type = 0; type <= maximumClass; type++)
        {
            _classDimensions[type] = (int)bits.Read(3) + 1;
            _classSubclasses[type] = (int)bits.Read(2);
            if (_classSubclasses[type] != 0)
            {
                _classMasterbooks[type] = CheckBook((int)bits.Read(8), books);
            }

            for (var subclass = 0; subclass < 1 << _classSubclasses[type]; subclass++)
            {
                var book = (int)bits.Read(8) - 1;
                _subclassBooks[type * 8 + subclass] = book < 0 ? -1 : CheckBook(book, books);
            }
        }

        _multiplier = (int)bits.Read(2) + 1;
        var rangeBits = (int)bits.Read(4);
        var x = new List<int> { 0, 1 << rangeBits };
        foreach (var type in _partitionClasses)
        {
            for (var dimension = 0; dimension < _classDimensions[type]; dimension++)
            {
                x.Add((int)bits.Read(rangeBits));
            }
        }

        if (bits.IsEnded || x.Count > MostValues || x.Distinct().Count() != x.Count)
        {
            throw Damaged("a floor's X values are more than 65, or not each its own");
        }

        _x = [.. x];
        _sorted = [.. Enumerable.Range(0, _x.Length).OrderBy(index => _x[index])];
        _low = new int[_x.Length];
        _high = new int[_x.Length];
        for (var index = 2; index < _x.Length; index++)
        {
            _low[index] = Neighbor(index, low: true);
            _high[index] = Neighbor(index, low: false);
        }

        _y = [.. Enumerable.Range(0, channels).Select(_ => new int[_x.Length])];
        _finalY = new int[_x.Length];
        _drawn = new bool[_x.Length];
    }

    // 7.2.3.
    public override bool Read(ref VorbisBits bits, VorbisCodebook[] books, int channel)
    {
        if (!bits.ReadFlag())
        {
            return false;
        }

        var y = _y[channel];
        var range = Ranges[_multiplier - 1];
        var rangeBits = VorbisCodebook.ILog(range - 1);
        y[0] = (int)bits.Read(rangeBits);
        y[1] = (int)bits.Read(rangeBits);
        var offset = 2;
        foreach (var type in _partitionClasses)
        {
            var dimensions = _classDimensions[type];
            var subclassBits = _classSubclasses[type];
            var subclassMask = (1 << subclassBits) - 1;
            var classValue = 0;
            if (subclassBits > 0)
            {
                classValue = books[_classMasterbooks[type]].ReadEntry(ref bits);
            }

            for (var dimension = 0; dimension < dimensions; dimension++)
            {
                var book = _subclassBooks[type * 8 + (classValue & subclassMask)];
                classValue >>= subclassBits;
                y[offset + dimension] = book >= 0 ? books[book].ReadEntry(ref bits) : 0;
            }

            offset += dimensions;
        }

        return !bits.IsEnded;
    }

    // 7.2.4: the final Y values from the differences, then the lines through those drawn to, each sample's value of
    // table 10.1 multiplied in.
    public override void Apply(int channel, Span<float> spectrum)
    {
        var y = _y[channel];
        var range = Ranges[_multiplier - 1];
        _drawn[0] = _drawn[1] = true;
        _finalY[0] = y[0];
        _finalY[1] = y[1];
        for (var index = 2; index < _x.Length; index++)
        {
            var low = _low[index];
            var high = _high[index];
            var predicted = RenderPoint(_x[low], _finalY[low], _x[high], _finalY[high], _x[index]);
            var value = y[index];
            var highRoom = range - predicted;
            var lowRoom = predicted;
            var room = (highRoom < lowRoom ? highRoom : lowRoom) * 2;
            if (value != 0)
            {
                _drawn[low] = _drawn[high] = _drawn[index] = true;
                if (value >= room)
                {
                    _finalY[index] = highRoom > lowRoom ? value - lowRoom + predicted : predicted - value + highRoom - 1;
                }
                else
                {
                    _finalY[index] = (value & 1) != 0 ? predicted - (value + 1) / 2 : predicted + value / 2;
                }
            }
            else
            {
                _drawn[index] = false;
                _finalY[index] = predicted;
            }
        }

        // Within the range, as a damaged setup could go past it.
        for (var index = 0; index < _x.Length; index++)
        {
            _finalY[index] = Math.Clamp(_finalY[index], 0, range - 1);
        }

        var lx = 0;
        var ly = _finalY[_sorted[0]] * _multiplier;
        var hx = 0;
        var hy = ly;
        for (var at = 1; at < _sorted.Length; at++)
        {
            var index = _sorted[at];
            if (_drawn[index])
            {
                hy = _finalY[index] * _multiplier;
                hx = _x[index];
                RenderLine(lx, ly, hx, hy, spectrum);
                lx = hx;
                ly = hy;
            }
        }

        if (hx < spectrum.Length)
        {
            RenderLine(hx, hy, spectrum.Length, hy, spectrum);
        }
    }

    // low_neighbor and high_neighbor of 9.2.4 and 9.2.5: of the values before, the nearest below and above.
    private int Neighbor(int index, bool low)
    {
        var found = -1;
        for (var other = 0; other < index; other++)
        {
            var isSide = low ? _x[other] < _x[index] : _x[other] > _x[index];
            if (isSide && (found < 0 || (low ? _x[other] > _x[found] : _x[other] < _x[found])))
            {
                found = other;
            }
        }

        return found;
    }

    // render_point of 9.2.6.
    private static int RenderPoint(int x0, int y0, int x1, int y1, int x)
    {
        var dy = y1 - y0;
        var adx = x1 - x0;
        var offset = Math.Abs(dy) * (x - x0) / adx;
        return dy < 0 ? y0 - offset : y0 + offset;
    }

    // render_line of 9.2.7, each value of the line multiplied in as table 10.1 has it; none past the spectrum.
    private static void RenderLine(int x0, int y0, int x1, int y1, Span<float> spectrum)
    {
        var dy = y1 - y0;
        var adx = x1 - x0;
        var ady = Math.Abs(dy);
        var step = dy / adx;
        var y = y0;
        var error = 0;
        var stepAside = dy < 0 ? step - 1 : step + 1;
        ady -= Math.Abs(step) * adx;
        var end = Math.Min(x1, spectrum.Length);
        if (x0 < end)
        {
            spectrum[x0] *= VorbisTables.InverseDb[Math.Clamp(y, 0, 255)];
        }

        for (var x = x0 + 1; x < end; x++)
        {
            error += ady;
            if (error >= adx)
            {
                error -= adx;
                y += stepAside;
            }
            else
            {
                y += step;
            }

            spectrum[x] *= VorbisTables.InverseDb[Math.Clamp(y, 0, 255)];
        }
    }
}
