namespace Adamantium.Pulsar.Decoding;

/// <summary>CELT's bit allocation (RFC 6716, 4.3.3): from the bits a frame has left, each band's bits for its shape,
/// in eighths, and for its fine energy, in whole bits a channel, with the symbols that steer it read on the way. It must
/// come out as the encoder's did, to the eighth, as every later symbol depends on it.</summary>
internal static class CeltAllocation
{
    private const int Resolution = CeltTables.BitResolution;
    private const int Steps = 6;

    /// <summary>The bands coded. <paramref name="pulses"/>, <paramref name="fineBits"/> and
    /// <paramref name="finePriority"/> are by band; <paramref name="balance"/> is what was left over.</summary>
    public static int Compute(int start, int end, ReadOnlySpan<int> boosts, ReadOnlySpan<int> caps, int trim,
        ref int intensity, ref bool dualStereo, int total, out int balance, Span<int> pulses, Span<int> fineBits,
        Span<int> finePriority, int channels, int shift, ref OpusRangeDecoder decoder)
    {
        var edges = CeltTables.BandEdges;
        total = Math.Max(total, 0);
        var skipStart = start;
        var skipReserved = total >= 1 << Resolution ? 1 << Resolution : 0;
        total -= skipReserved;
        var intensityReserved = 0;
        var dualReserved = 0;
        if (channels == 2)
        {
            intensityReserved = CeltTables.Log2Eighths[end - start];
            if (intensityReserved > total)
            {
                intensityReserved = 0;
            }
            else
            {
                total -= intensityReserved;
                dualReserved = total >= 1 << Resolution ? 1 << Resolution : 0;
                total -= dualReserved;
            }
        }

        Span<int> bits1 = stackalloc int[CeltTables.Bands];
        Span<int> bits2 = stackalloc int[CeltTables.Bands];
        Span<int> thresholds = stackalloc int[CeltTables.Bands];
        Span<int> trimOffsets = stackalloc int[CeltTables.Bands];
        for (var band = start; band < end; band++)
        {
            var width = edges[band + 1] - edges[band];
            thresholds[band] = Math.Max(channels << Resolution, (3 * width << shift << Resolution) >> 4);
            trimOffsets[band] = channels * width * (trim - 5 - shift) * (end - band - 1) * (1 << (shift + Resolution)) >> 6;
            if (width << shift == 1)
            {
                trimOffsets[band] -= channels << Resolution;
            }
        }

        // The highest quality that fits, searched.
        var low = 1;
        var high = CeltTables.AllocationVectors - 1;
        do
        {
            var isDone = false;
            var sum = 0;
            var middle = (low + high) >> 1;
            for (var band = end - 1; band >= start; band--)
            {
                var bits = StaticBits(middle, band, channels, shift);
                if (bits > 0)
                {
                    bits = Math.Max(0, bits + trimOffsets[band]);
                }

                bits += boosts[band];
                if (bits >= thresholds[band] || isDone)
                {
                    isDone = true;
                    sum += Math.Min(bits, caps[band]);
                }
                else if (bits >= channels << Resolution)
                {
                    sum += channels << Resolution;
                }
            }

            if (sum > total)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }
        while (low <= high);

        high = low--;
        for (var band = start; band < end; band++)
        {
            var lowBits = StaticBits(low, band, channels, shift);
            var highBits = high >= CeltTables.AllocationVectors ? caps[band] : StaticBits(high, band, channels, shift);
            if (lowBits > 0)
            {
                lowBits = Math.Max(0, lowBits + trimOffsets[band]);
            }

            if (highBits > 0)
            {
                highBits = Math.Max(0, highBits + trimOffsets[band]);
            }

            if (low > 0)
            {
                lowBits += boosts[band];
            }

            highBits += boosts[band];
            if (boosts[band] > 0)
            {
                skipStart = band;
            }

            bits1[band] = lowBits;
            bits2[band] = Math.Max(0, highBits - lowBits);
        }

        return Interpolate(start, end, skipStart, bits1, bits2, thresholds, caps, total, out balance, skipReserved,
            ref intensity, intensityReserved, ref dualStereo, dualReserved, pulses, fineBits, finePriority, channels,
            shift, ref decoder);
    }

    private static int StaticBits(int quality, int band, int channels, int shift)
    {
        var width = CeltTables.BandEdges[band + 1] - CeltTables.BandEdges[band];
        return channels * width * CeltTables.Allocations[quality * CeltTables.Bands + band] << shift >> 2;
    }

    // interp_bits2pulses: between the two qualities in 64ths, then the bands skipped from the top, then what is left
    // spread, and each band's split between fine energy and shape.
    private static int Interpolate(int start, int end, int skipStart, scoped ReadOnlySpan<int> bits1,
        scoped ReadOnlySpan<int> bits2, scoped ReadOnlySpan<int> thresholds, ReadOnlySpan<int> caps, int total, out int balance, int skipReserved,
        ref int intensity, int intensityReserved, ref bool dualStereo, int dualReserved, Span<int> bits,
        Span<int> fineBits, Span<int> finePriority, int channels, int shift, ref OpusRangeDecoder decoder)
    {
        var edges = CeltTables.BandEdges;
        var floor = channels << Resolution;
        var stereo = channels > 1 ? 1 : 0;
        var logShift = shift << Resolution;
        var low = 0;
        var high = 1 << Steps;
        for (var step = 0; step < Steps; step++)
        {
            var middle = (low + high) >> 1;
            var sum = 0;
            var isDone = false;
            for (var band = end - 1; band >= start; band--)
            {
                var value = bits1[band] + (middle * bits2[band] >> Steps);
                if (value >= thresholds[band] || isDone)
                {
                    isDone = true;
                    sum += Math.Min(value, caps[band]);
                }
                else if (value >= floor)
                {
                    sum += floor;
                }
            }

            if (sum > total)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        var total2 = 0;
        var done = false;
        for (var band = end - 1; band >= start; band--)
        {
            var value = bits1[band] + (low * bits2[band] >> Steps);
            if (value < thresholds[band] && !done)
            {
                value = value >= floor ? floor : 0;
            }
            else
            {
                done = true;
            }

            value = Math.Min(value, caps[band]);
            bits[band] = value;
            total2 += value;
        }

        int coded;
        for (coded = end; ; coded--)
        {
            var band = coded - 1;
            if (band <= skipStart)
            {
                total += skipReserved;
                break;
            }

            var left = total - total2;
            var perBin = left / (edges[coded] - edges[start]);
            left -= (edges[coded] - edges[start]) * perBin;
            var rest = Math.Max(left - (edges[band] - edges[start]), 0);
            var width = edges[coded] - edges[band];
            var bandBits = bits[band] + perBin * width + rest;
            if (bandBits >= Math.Max(thresholds[band], floor + (1 << Resolution)))
            {
                if (decoder.DecodeBit(1))
                {
                    break;
                }

                total2 += 1 << Resolution;
                bandBits -= 1 << Resolution;
            }

            total2 -= bits[band] + intensityReserved;
            if (intensityReserved > 0)
            {
                intensityReserved = CeltTables.Log2Eighths[band - start];
            }

            total2 += intensityReserved;
            if (bandBits >= floor)
            {
                total2 += floor;
                bits[band] = floor;
            }
            else
            {
                bits[band] = 0;
            }
        }

        if (intensityReserved > 0)
        {
            intensity = start + (int)decoder.DecodeUniform((uint)(coded + 1 - start));
        }
        else
        {
            intensity = 0;
        }

        if (intensity <= start)
        {
            total += dualReserved;
            dualReserved = 0;
        }

        dualStereo = dualReserved > 0 && decoder.DecodeBit(1);

        var remaining = total - total2;
        var each = remaining / (edges[coded] - edges[start]);
        remaining -= (edges[coded] - edges[start]) * each;
        for (var band = start; band < coded; band++)
        {
            bits[band] += each * (edges[band + 1] - edges[band]);
        }

        for (var band = start; band < coded; band++)
        {
            var extra = Math.Min(remaining, edges[band + 1] - edges[band]);
            bits[band] += extra;
            remaining -= extra;
        }

        balance = 0;
        int at;
        for (at = start; at < coded; at++)
        {
            var width0 = edges[at + 1] - edges[at];
            var width = width0 << shift;
            bits[at] += balance;
            int excess;
            if (width > 1)
            {
                excess = Math.Max(bits[at] - caps[at], 0);
                bits[at] -= excess;
                var den = channels * width + (channels == 2 && width > 2 && !dualStereo && at < intensity ? 1 : 0);
                var logTotal = den * (CeltTables.LogWidths[at] + logShift);
                var offset = (logTotal >> 1) - den * CeltTables.FineOffset;
                if (width == 2)
                {
                    offset += den << Resolution >> 2;
                }

                if (bits[at] + offset < den * 2 << Resolution)
                {
                    offset += logTotal >> 2;
                }
                else if (bits[at] + offset < den * 3 << Resolution)
                {
                    offset += logTotal >> 3;
                }

                fineBits[at] = Math.Max(0, (bits[at] + offset + (den << (Resolution - 1))) / (den << Resolution));
                if (channels * fineBits[at] > bits[at] >> Resolution)
                {
                    fineBits[at] = bits[at] >> stereo >> Resolution;
                }

                fineBits[at] = Math.Min(fineBits[at], CeltTables.MostFineBits);
                finePriority[at] = fineBits[at] * (den << Resolution) >= bits[at] + offset ? 1 : 0;
                bits[at] -= channels * fineBits[at] << Resolution;
            }
            else
            {
                excess = Math.Max(0, bits[at] - (channels << Resolution));
                bits[at] -= excess;
                fineBits[at] = 0;
                finePriority[at] = 1;
            }

            if (excess > 0)
            {
                var extraFine = Math.Min(excess >> (stereo + Resolution), CeltTables.MostFineBits - fineBits[at]);
                fineBits[at] += extraFine;
                var extraBits = extraFine * channels << Resolution;
                finePriority[at] = extraBits >= excess - balance ? 1 : 0;
                excess -= extraBits;
            }

            balance = excess;
        }

        for (; at < end; at++)
        {
            fineBits[at] = bits[at] >> stereo >> Resolution;
            bits[at] = 0;
            finePriority[at] = fineBits[at] < 1 ? 1 : 0;
        }

        return coded;
    }

    /// <summary>bits2pulses: of a band's bits, the pseudo pulse count whose cost is nearest.</summary>
    public static int PulsesOf(int band, int shift, int bits)
    {
        var costs = CeltTables.PulseCosts[CeltTables.PulseCostStarts[(shift + 1) * CeltTables.Bands + band]..];
        var low = 0;
        int high = costs[0];
        bits--;
        for (var step = 0; step < CeltTables.LogMostPseudo; step++)
        {
            var middle = (low + high + 1) >> 1;
            if (costs[middle] >= bits)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return bits - (low == 0 ? -1 : costs[low]) <= costs[high] - bits ? low : high;
    }

    /// <summary>pulses2bits: what a pseudo pulse count costs, in eighths.</summary>
    public static int BitsOf(int band, int shift, int pulses) =>
        pulses == 0 ? 0 : CeltTables.PulseCosts[CeltTables.PulseCostStarts[(shift + 1) * CeltTables.Bands + band] + pulses] + 1;

    /// <summary>get_pulses: a pseudo pulse count's pulses.</summary>
    public static int RealPulses(int pseudo) => pseudo < 8 ? pseudo : (8 + (pseudo & 7)) << ((pseudo >> 3) - 1);
}
