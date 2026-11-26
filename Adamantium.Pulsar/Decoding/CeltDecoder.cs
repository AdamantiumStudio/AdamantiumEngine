namespace Adamantium.Pulsar.Decoding;

/// <summary>The CELT layer of Opus (RFC 6716, 4.3), at 48 kHz: of each frame its energies coarse and fine, its bit
/// allocation, the shapes of its bands, then the inverse MDCT, the pitch post-filter and the de-emphasis, into samples
/// of 16-bit scale. What a frame needs of those before it - energies, the overlap, the filter's history - it keeps.
/// </summary>
internal sealed class CeltDecoder
{
    private const int Resolution = CeltTables.BitResolution;
    private const int BufferLength = 2048;
    private const int LongestPeriod = 1024;
    private const int ShortestPeriod = 15;
    private const int Bands = CeltTables.Bands;
    private const float Emphasis = .85000610f;

    private readonly int _channels;
    private readonly CeltBands _bands = new();
    private readonly CeltTransform _transform = new();
    // By channel: BufferLength of history and output, then the overlap kept for the next frame.
    private readonly float[][] _memory;
    private readonly float[] _energies = new float[2 * Bands];
    private readonly float[] _logEnergies = new float[2 * Bands];
    private readonly float[] _logEnergies2 = new float[2 * Bands];
    private readonly float[] _spectrum = new float[2 * 960];
    private readonly float[] _shapes = new float[2 * 960];
    private readonly float[] _block = new float[960 + CeltTables.Overlap];
    private readonly float[] _bandEnergies = new float[2 * Bands];
    private readonly float[] _emphasis = new float[2];
    private readonly int[] _timeFrequency = new int[Bands];
    private readonly int[] _caps = new int[Bands];
    private readonly int[] _boosts = new int[Bands];
    private readonly int[] _pulses = new int[Bands];
    private readonly int[] _fineBits = new int[Bands];
    private readonly int[] _finePriority = new int[Bands];
    private readonly byte[] _collapseMasks = new byte[2 * Bands];
    private int _period;
    private int _oldPeriod;
    private float _gain;
    private float _oldGain;
    private int _tapset;
    private int _oldTapset;
    private uint _seed;

    public CeltDecoder(int channels)
    {
        _channels = channels;
        _memory = [.. Enumerable.Range(0, channels).Select(_ => new float[BufferLength + CeltTables.Overlap])];
        Reset();
    }

    /// <summary>The first band coded: 0, or 17 in hybrid frames.</summary>
    public int StartBand { get; set; }

    /// <summary>One past the last band coded, by the frame's bandwidth.</summary>
    public int EndBand { get; set; } = Bands;

    /// <summary>The channels coded, 1 or 2; a mono frame is heard in both of two.</summary>
    public int StreamChannels { get; set; }

    public void Reset()
    {
        foreach (var memory in _memory)
        {
            Array.Clear(memory);
        }

        Array.Clear(_energies);
        Array.Fill(_logEnergies, -28f);
        Array.Fill(_logEnergies2, -28f);
        Array.Clear(_emphasis);
        _period = _oldPeriod = _tapset = _oldTapset = 0;
        _gain = _oldGain = 0;
        _seed = 0;
    }

    /// <summary>A frame of <paramref name="frameSize"/> samples a channel, 120 to 960, from the range decoder, into
    /// <paramref name="output"/>, the channels side by side, added to what is there when
    /// <paramref name="isAdded"/>. Its state where it ended is the range decoder's.</summary>
    public void Decode(ref OpusRangeDecoder decoder, Span<float> output, int frameSize, bool isAdded)
    {
        var shift = 0;
        while (CeltTables.ShortBlock << shift != frameSize)
        {
            shift++;
        }

        var scale = 1 << shift;
        var size = scale * CeltTables.ShortBlock;
        var channels = StreamChannels;
        var start = StartBand;
        var end = EndBand;
        var edges = CeltTables.BandEdges;
        var x = _shapes.AsSpan(0, size);
        var y = channels == 2 ? _shapes.AsSpan(size, size) : default;
        _shapes.AsSpan(0, 2 * size).Clear();
        if (channels == 1)
        {
            for (var band = 0; band < Bands; band++)
            {
                _energies[band] = Math.Max(_energies[band], _energies[Bands + band]);
            }
        }

        var length = decoder.Length;
        var totalBits = length * 8;
        var tell = decoder.Tell();
        var isSilent = tell >= totalBits || tell == 1 && decoder.DecodeBit(15);
        if (isSilent)
        {
            // The rest is as if read.
            decoder.MarkAllRead();
            tell = totalBits;
        }

        var period = 0;
        var gain = 0f;
        var tapset = 0;
        if (start == 0 && tell + 16 <= totalBits)
        {
            if (decoder.DecodeBit(1))
            {
                var octave = (int)decoder.DecodeUniform(6);
                period = (16 << octave) + (int)decoder.ReadBits(4 + octave) - 1;
                var quantized = (int)decoder.ReadBits(3);
                if (decoder.Tell() + 2 <= totalBits)
                {
                    tapset = decoder.DecodeInverse(CeltTables.TapsetInverse, 2);
                }

                gain = .09375f * (quantized + 1);
            }

            tell = decoder.Tell();
        }

        var isTransient = false;
        if (shift > 0 && tell + 3 <= totalBits)
        {
            isTransient = decoder.DecodeBit(3);
            tell = decoder.Tell();
        }

        var shortBlocks = isTransient ? scale : 0;
        var isIntra = tell + 3 <= totalBits && decoder.DecodeBit(3);
        DecodeCoarseEnergy(start, end, isIntra, ref decoder, channels, shift);
        DecodeTimeFrequency(start, end, isTransient, shift, ref decoder);
        tell = decoder.Tell();
        var spread = 2;
        if (tell + 4 <= totalBits)
        {
            spread = decoder.DecodeInverse(CeltTables.SpreadInverse, 5);
        }

        for (var band = 0; band < Bands; band++)
        {
            var width = (edges[band + 1] - edges[band]) << shift;
            _caps[band] = (CeltTables.Caps[Bands * (2 * shift + channels - 1) + band] + 64) * channels * width >> 2;
        }

        var boostCost = 6;
        var total = totalBits << Resolution;
        var used = decoder.TellFraction();
        Array.Clear(_boosts);
        for (var band = start; band < end; band++)
        {
            var width = channels * (edges[band + 1] - edges[band]) << shift;
            var quanta = Math.Min(width << Resolution, Math.Max(6 << Resolution, width));
            var loopCost = boostCost;
            var boost = 0;
            while (used + (loopCost << Resolution) < total && boost < _caps[band])
            {
                var isBoosted = decoder.DecodeBit(loopCost);
                used = decoder.TellFraction();
                if (!isBoosted)
                {
                    break;
                }

                boost += quanta;
                total -= quanta;
                loopCost = 1;
            }

            _boosts[band] = boost;
            if (boost > 0)
            {
                boostCost = Math.Max(2, boostCost - 1);
            }
        }

        var trim = used + (6 << Resolution) <= total ? decoder.DecodeInverse(CeltTables.TrimInverse, 7) : 5;
        var bits = (length * 8 << Resolution) - decoder.TellFraction() - 1;
        var antiCollapseReserved = isTransient && shift >= 2 && bits >= (shift + 2) << Resolution ? 1 << Resolution : 0;
        bits -= antiCollapseReserved;
        var intensity = 0;
        var dualStereo = false;
        var coded = CeltAllocation.Compute(start, end, _boosts, _caps, trim, ref intensity, ref dualStereo, bits,
            out var balance, _pulses, _fineBits, _finePriority, channels, shift, ref decoder);
        DecodeFineEnergy(start, end, ref decoder, channels);
        _bands.Decode(start, end, x, y, _collapseMasks, _pulses, shortBlocks, spread, dualStereo, intensity,
            _timeFrequency, (length * (8 << Resolution)) - antiCollapseReserved, balance, ref decoder, shift, coded,
            ref _seed);
        var isAntiCollapse = antiCollapseReserved > 0 && decoder.ReadBits(1) != 0;
        FinishEnergy(start, end, length * 8 - decoder.Tell(), ref decoder, channels);
        if (isAntiCollapse)
        {
            AntiCollapse(x, y, shift, channels, size, start, end);
        }

        for (var channel = 0; channel < channels; channel++)
        {
            for (var band = 0; band < Bands; band++)
            {
                var index = channel * Bands + band;
                _bandEnergies[index] = band < start || band >= end
                    ? 0
                    // RFC 8251, 8: no energy past what a float holds.
                    : float.Exp2(Math.Min(32f, _energies[index] + CeltTables.EnergyMeans[band]));
            }
        }

        if (isSilent)
        {
            Array.Clear(_bandEnergies);
            Array.Fill(_energies, -28f);
        }

        // The spectrum: each band's shape times its energy.
        var spectrum = _spectrum.AsSpan(0, Math.Max(_channels, channels) * size);
        spectrum.Clear();
        for (var channel = 0; channel < channels; channel++)
        {
            var shape = channel == 0 ? x : y;
            var lines = spectrum.Slice(channel * size, size);
            for (var band = start; band < end; band++)
            {
                var energy = _bandEnergies[channel * Bands + band];
                for (var at = scale * edges[band]; at < scale * edges[band + 1]; at++)
                {
                    lines[at] = shape[at] * energy;
                }
            }
        }

        if (_channels == 2 && channels == 1)
        {
            spectrum[..size].CopyTo(spectrum[size..]);
        }
        else if (_channels == 1 && channels == 2)
        {
            for (var at = 0; at < size; at++)
            {
                spectrum[at] = .5f * (spectrum[at] + spectrum[size + at]);
            }
        }

        Synthesize(spectrum, size, shortBlocks, shift, period, gain, tapset, output, isAdded);
        if (channels == 1)
        {
            _energies.AsSpan(0, Bands).CopyTo(_energies.AsSpan(Bands));
        }

        if (!isTransient)
        {
            _logEnergies.CopyTo(_logEnergies2, 0);
            _energies.CopyTo(_logEnergies, 0);
        }
        else
        {
            for (var index = 0; index < 2 * Bands; index++)
            {
                _logEnergies[index] = Math.Min(_logEnergies[index], _energies[index]);
            }
        }

        for (var channel = 0; channel < 2; channel++)
        {
            for (var band = 0; band < Bands; band++)
            {
                if (band < start || band >= end)
                {
                    var index = channel * Bands + band;
                    _energies[index] = 0;
                    _logEnergies[index] = _logEnergies2[index] = -28f;
                }
            }
        }

        _seed = decoder.FinalRange;
    }

    /// <summary>A frame lost: what the overlap held, fading, and silence after it.</summary>
    public void Conceal(Span<float> output, int frameSize, bool isAdded)
    {
        var size = frameSize;
        var spectrum = _spectrum.AsSpan(0, _channels * size);
        spectrum.Clear();
        var shift = 0;
        while (CeltTables.ShortBlock << shift != size)
        {
            shift++;
        }

        Synthesize(spectrum, size, 0, shift, 0, 0, 0, output, isAdded);
    }

    // The inverse MDCTs, the post-filter on what they gave, and the de-emphasis into output.
    private void Synthesize(Span<float> spectrum, int size, int shortBlocks, int shift, int period, float gain,
        int tapset, Span<float> output, bool isAdded)
    {
        var overlap = CeltTables.Overlap;
        for (var channel = 0; channel < _channels; channel++)
        {
            var memory = _memory[channel];
            Array.Copy(memory, size, memory, 0, BufferLength - size);
            var block = _block.AsSpan(0, size + overlap);
            block[..overlap].Clear();
            var lines = spectrum.Slice(channel * size, size);
            if (shortBlocks != 0)
            {
                for (var at = 0; at < shortBlocks; at++)
                {
                    _transform.Inverse(lines[at..], shortBlocks, block[(CeltTables.ShortBlock * at)..], CeltTables.LongestShift);
                }
            }
            else
            {
                _transform.Inverse(lines, 1, block, CeltTables.LongestShift - shift);
            }

            var synthesis = BufferLength - size;
            var kept = memory.AsSpan(BufferLength, overlap);
            for (var at = 0; at < overlap; at++)
            {
                memory[synthesis + at] = block[at] + kept[at];
            }

            block[overlap..size].CopyTo(memory.AsSpan(synthesis + overlap));
            block.Slice(size, overlap).CopyTo(kept);
            _period = Math.Max(_period, ShortestPeriod);
            _oldPeriod = Math.Max(_oldPeriod, ShortestPeriod);
            CombFilter(memory, synthesis, _oldPeriod, _period, CeltTables.ShortBlock, _oldGain, _gain, _oldTapset,
                _tapset, overlap);
            if (shift != 0)
            {
                CombFilter(memory, synthesis + CeltTables.ShortBlock, _period, period, size - CeltTables.ShortBlock,
                    _gain, gain, _tapset, tapset, overlap);
            }
        }

        _oldPeriod = _period;
        _oldGain = _gain;
        _oldTapset = _tapset;
        _period = period;
        _gain = gain;
        _tapset = tapset;
        if (shift != 0)
        {
            _oldPeriod = _period;
            _oldGain = _gain;
            _oldTapset = _tapset;
        }

        for (var channel = 0; channel < _channels; channel++)
        {
            var memory = _memory[channel];
            var state = _emphasis[channel];
            for (var at = 0; at < size; at++)
            {
                var value = memory[BufferLength - size + at] + state;
                state = Emphasis * value;
                var sample = value * (1f / 32768);
                ref var target = ref output[at * _channels + channel];
                target = isAdded ? target + sample : sample;
            }

            _emphasis[channel] = state;
        }
    }

    // The pitch post-filter, in place: from the old filter to the new over the overlap, then the new one.
    private static void CombFilter(float[] x, int at, int oldPeriod, int period, int count, float oldGain, float gain,
        int oldTapset, int tapset, int overlap)
    {
        ReadOnlySpan<float> taps = [.3066406250f, .2170410156f, .1296386719f, .4638671875f, .2680664062f, 0, .7998046875f, .1000976562f, 0];
        var g00 = oldGain * taps[oldTapset * 3];
        var g01 = oldGain * taps[oldTapset * 3 + 1];
        var g02 = oldGain * taps[oldTapset * 3 + 2];
        var g10 = gain * taps[tapset * 3];
        var g11 = gain * taps[tapset * 3 + 1];
        var g12 = gain * taps[tapset * 3 + 2];
        var window = CeltTables.Window;
        var index = 0;
        for (; index < overlap && index < count; index++)
        {
            var i = at + index;
            var f = window[index] * window[index];
            var rest = 1 - f;
            x[i] = x[i] + rest * g00 * x[i - oldPeriod] + rest * g01 * x[i - oldPeriod - 1] +
                rest * g01 * x[i - oldPeriod + 1] + rest * g02 * x[i - oldPeriod - 2] +
                rest * g02 * x[i - oldPeriod + 2] + f * g10 * x[i - period] + f * g11 * x[i - period - 1] +
                f * g11 * x[i - period + 1] + f * g12 * x[i - period - 2] + f * g12 * x[i - period + 2];
        }

        for (; index < count; index++)
        {
            var i = at + index;
            x[i] = x[i] + g10 * x[i - period] + g11 * x[i - period - 1] + g11 * x[i - period + 1] +
                g12 * x[i - period - 2] + g12 * x[i - period + 2];
        }
    }

    // unquant_coarse_energy: each band's energy in 6 dB steps, predicted from the frame before and the band below.
    private void DecodeCoarseEnergy(int start, int end, bool isIntra, ref OpusRangeDecoder decoder, int channels, int shift)
    {
        var model = CeltTables.EnergyModel.Slice((shift * 2 + (isIntra ? 1 : 0)) * 42, 42);
        Span<float> previous = stackalloc float[2];
        var coefficient = isIntra ? 0 : CeltTables.TimePrediction[shift];
        var beta = isIntra ? CeltTables.IntraBandPrediction : CeltTables.BandPrediction[shift];
        var budget = decoder.Length * 8;
        for (var band = start; band < end; band++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var tell = decoder.Tell();
                int value;
                if (budget - tell >= 15)
                {
                    var at = 2 * Math.Min(band, 20);
                    value = DecodeLaplace(ref decoder, (uint)model[at] << 7, model[at + 1] << 6);
                }
                else if (budget - tell >= 2)
                {
                    value = decoder.DecodeInverse(CeltTables.SmallEnergyInverse, 2);
                    value = (value >> 1) ^ -(value & 1);
                }
                else if (budget - tell >= 1)
                {
                    value = decoder.DecodeBit(1) ? -1 : 0;
                }
                else
                {
                    value = -1;
                }

                var index = band + channel * Bands;
                _energies[index] = Math.Max(-9f, _energies[index]);
                _energies[index] = coefficient * _energies[index] + previous[channel] + value;
                previous[channel] = previous[channel] + value - beta * value;
            }
        }
    }

    // ec_laplace_decode.
    private static int DecodeLaplace(ref OpusRangeDecoder decoder, uint frequency, int decay)
    {
        const uint least = 1;
        var value = 0;
        var found = decoder.DecodeBinary(15);
        uint low = 0;
        if (found >= frequency)
        {
            value++;
            low = frequency;
            frequency = ((32768 - least * 2 * 16 - frequency) * (uint)(16384 - decay) >> 15) + least;
            while (frequency > least && found >= low + 2 * frequency)
            {
                frequency *= 2;
                low += frequency;
                frequency = (uint)((frequency - 2 * least) * decay >> 15);
                frequency += least;
                value++;
            }

            if (frequency <= least)
            {
                var more = (int)((found - low) >> 1);
                value += more;
                low += (uint)(2 * more) * least;
            }

            if (found < low + frequency)
            {
                value = -value;
            }
            else
            {
                low += frequency;
            }
        }

        decoder.Update(low, Math.Min(low + frequency, 32768), 32768);
        return value;
    }

    private void DecodeTimeFrequency(int start, int end, bool isTransient, int shift, ref OpusRangeDecoder decoder)
    {
        var budget = decoder.Length * 8;
        var tell = decoder.Tell();
        var cost = isTransient ? 2 : 4;
        var isSelectReserved = shift > 0 && tell + cost + 1 <= budget;
        budget -= isSelectReserved ? 1 : 0;
        var changed = 0;
        var current = 0;
        for (var band = start; band < end; band++)
        {
            if (tell + cost <= budget)
            {
                current ^= decoder.DecodeBit(cost) ? 1 : 0;
                tell = decoder.Tell();
                changed |= current;
            }

            _timeFrequency[band] = current;
            cost = isTransient ? 4 : 5;
        }

        var select = 0;
        var table = CeltTables.TimeFrequencySelect.Slice(shift * 8 + 4 * (isTransient ? 1 : 0), 4);
        if (isSelectReserved && table[changed] != table[2 + changed])
        {
            select = decoder.DecodeBit(1) ? 1 : 0;
        }

        for (var band = start; band < end; band++)
        {
            _timeFrequency[band] = table[2 * select + _timeFrequency[band]];
        }
    }

    private void DecodeFineEnergy(int start, int end, ref OpusRangeDecoder decoder, int channels)
    {
        for (var band = start; band < end; band++)
        {
            var bits = _fineBits[band];
            if (bits <= 0)
            {
                continue;
            }

            for (var channel = 0; channel < channels; channel++)
            {
                var value = decoder.ReadBits(bits);
                _energies[band + channel * Bands] += (value + .5f) * (1 << (14 - bits)) * (1f / 16384) - .5f;
            }
        }
    }

    // unquant_energy_finalise: the bits left, one a band and channel, by priority.
    private void FinishEnergy(int start, int end, int bitsLeft, ref OpusRangeDecoder decoder, int channels)
    {
        for (var priority = 0; priority < 2; priority++)
        {
            for (var band = start; band < end && bitsLeft >= channels; band++)
            {
                if (_fineBits[band] >= CeltTables.MostFineBits || _finePriority[band] != priority)
                {
                    continue;
                }

                for (var channel = 0; channel < channels; channel++)
                {
                    var value = decoder.ReadBits(1);
                    _energies[band + channel * Bands] += (value - .5f) * (1 << (14 - _fineBits[band] - 1)) * (1f / 16384);
                    bitsLeft--;
                }
            }
        }
    }

    // anti_collapse: noise in the short blocks of a band that have no pulses.
    private void AntiCollapse(Span<float> x, Span<float> y, int shift, int channels, int size, int start, int end)
    {
        var edges = CeltTables.BandEdges;
        var seed = _seed;
        for (var band = start; band < end; band++)
        {
            var width = edges[band + 1] - edges[band];
            var depth = (1 + _pulses[band]) / (width << shift);
            var threshold = .5f * float.Exp2(-.125f * depth);
            var inverseRoot = 1 / MathF.Sqrt(width << shift);
            for (var channel = 0; channel < channels; channel++)
            {
                var previous1 = _logEnergies[channel * Bands + band];
                var previous2 = _logEnergies2[channel * Bands + band];
                if (channels == 1)
                {
                    previous1 = Math.Max(previous1, _logEnergies[Bands + band]);
                    previous2 = Math.Max(previous2, _logEnergies2[Bands + band]);
                }

                var difference = Math.Max(0, _energies[channel * Bands + band] - Math.Min(previous1, previous2));
                var r = 2 * float.Exp2(-difference);
                if (shift == 3)
                {
                    r *= 1.41421356f;
                }

                r = Math.Min(threshold, r) * inverseRoot;
                var shape = (channel == 0 ? x : y)[(edges[band] << shift)..];
                var isChanged = false;
                for (var block = 0; block < 1 << shift; block++)
                {
                    if ((_collapseMasks[band * channels + channel] & (1 << block)) == 0)
                    {
                        for (var at = 0; at < width; at++)
                        {
                            seed = CeltBands.NextRandom(seed);
                            shape[(at << shift) + block] = (seed & 0x8000) != 0 ? r : -r;
                        }

                        isChanged = true;
                    }
                }

                if (isChanged)
                {
                    CeltBands.Renormalize(shape[..(width << shift)], 1);
                }
            }
        }
    }
}
