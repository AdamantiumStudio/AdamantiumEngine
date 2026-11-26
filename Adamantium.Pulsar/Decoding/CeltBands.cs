namespace Adamantium.Pulsar.Decoding;

/// <summary>The shapes of CELT's bands (RFC 6716, 4.3.4): each band's unit vector of pulses, split in halves by a coded
/// angle where it would cost more than 32 bits, folded from the bands below where it has no pulses, rotated by the
/// spread, and recombined in time and frequency as its resolution says; two channels as mid and side, intensity or
/// both apart. The splits recurse no deeper than the shift and the stereo split allow.</summary>
internal sealed class CeltBands
{
    private const int Resolution = CeltTables.BitResolution;
    private const int SpreadNone = 0;
    private const int SpreadAggressive = 3;
    private const int LongestBin = 8 * 100;

    // The bands decoded, of each channel, folded into those above.
    private readonly float[] _norm = new float[2 * LongestBin];
    private readonly float[] _scratch = new float[8 * 22];
    private readonly float[] _hadamard = new float[LongestBin];
    private readonly int[] _pulses = new int[176];
    private uint _seed;
    private int _remaining;
    private int _spread;
    private int _intensity;
    private int _shift;

    /// <summary>All bands from <paramref name="start"/> to <paramref name="end"/> into <paramref name="x"/> and, for
    /// two channels, <paramref name="y"/>; their collapse masks by band and channel. The random seed goes on from
    /// frame to frame.</summary>
    public void Decode(int start, int end, Span<float> x, Span<float> y, Span<byte> collapseMasks,
        ReadOnlySpan<int> pulses, int shortBlocks, int spread, bool dualStereo, int intensity,
        ReadOnlySpan<int> timeFrequency, int totalBits, int balance, ref OpusRangeDecoder decoder, int shift,
        int codedBands, ref uint seed)
    {
        var edges = CeltTables.BandEdges;
        var channels = y.IsEmpty ? 1 : 2;
        var scale = 1 << shift;
        var blocks = shortBlocks != 0 ? scale : 1;
        var norm = _norm.AsSpan(0, scale * edges[CeltTables.Bands]);
        var norm2 = _norm.AsSpan(scale * edges[CeltTables.Bands], scale * edges[CeltTables.Bands]);
        _seed = seed;
        _spread = spread;
        _intensity = intensity;
        _shift = shift;
        var lowbandOffset = 0;
        var updateLowband = true;
        for (var band = start; band < end; band++)
        {
            var bins = scale * (edges[band + 1] - edges[band]);
            var bandX = x.Slice(scale * edges[band], bins);
            var bandY = channels == 2 ? y.Slice(scale * edges[band], bins) : default;
            var tell = decoder.TellFraction();
            if (band != start)
            {
                balance -= tell;
            }

            _remaining = totalBits - tell - 1;
            var bits = 0;
            if (band <= codedBands - 1)
            {
                var currentBalance = balance / Math.Min(3, codedBands - band);
                bits = Math.Max(0, Math.Min(16383, Math.Min(_remaining + 1, pulses[band] + currentBalance)));
            }

            // RFC 8251, 9: the second band folds from the first even where the first is narrower.
            if (scale * edges[band] - bins >= scale * edges[start] || band == start + 1)
            {
                if (updateLowband || lowbandOffset == 0)
                {
                    lowbandOffset = band;
                }
            }

            if (band == start + 1)
            {
                var first = scale * (edges[start + 1] - edges[start]);
                var second = scale * (edges[start + 2] - edges[start + 1]);
                var offset = scale * edges[start];
                if (second > first)
                {
                    norm.Slice(offset + 2 * first - second, second - first).CopyTo(norm[(offset + first)..]);
                    if (channels == 2)
                    {
                        norm2.Slice(offset + 2 * first - second, second - first).CopyTo(norm2[(offset + first)..]);
                    }
                }
            }

            var change = timeFrequency[band];
            var effectiveLowband = -1;
            uint xMask;
            uint yMask;
            if (lowbandOffset != 0 && (spread != SpreadAggressive || blocks > 1 || change < 0))
            {
                effectiveLowband = Math.Max(scale * edges[start], scale * edges[lowbandOffset] - bins);
                var foldStart = lowbandOffset;
                while (scale * edges[--foldStart] > effectiveLowband)
                {
                }

                var foldEnd = lowbandOffset - 1;
                while (++foldEnd < band && scale * edges[foldEnd] < effectiveLowband + bins)
                {
                }

                xMask = yMask = 0;
                var fold = foldStart;
                do
                {
                    xMask |= collapseMasks[fold * channels];
                    yMask |= collapseMasks[fold * channels + channels - 1];
                }
                while (++fold < foldEnd);
            }
            else
            {
                xMask = yMask = (1u << blocks) - 1;
            }

            if (dualStereo && band == intensity)
            {
                dualStereo = false;
                for (var at = scale * edges[start]; at < scale * edges[band]; at++)
                {
                    norm[at] = .5f * (norm[at] + norm2[at]);
                }
            }

            var outX = norm.Slice(scale * edges[band], bins);
            if (dualStereo)
            {
                xMask = DecodeBand(band, bandX, default, bins, bits / 2, blocks, change,
                    effectiveLowband != -1 ? norm[effectiveLowband..] : default, ref decoder, shift, outX, 0, 1f,
                    xMask);
                yMask = DecodeBand(band, bandY, default, bins, bits / 2, blocks, change,
                    effectiveLowband != -1 ? norm2[effectiveLowband..] : default, ref decoder, shift,
                    norm2.Slice(scale * edges[band], bins), 0, 1f, yMask);
            }
            else
            {
                xMask = DecodeBand(band, bandX, bandY, bins, bits, blocks, change,
                    effectiveLowband != -1 ? norm[effectiveLowband..] : default, ref decoder, shift, outX, 0, 1f,
                    xMask | yMask);
                yMask = xMask;
            }

            collapseMasks[band * channels] = (byte)xMask;
            collapseMasks[band * channels + channels - 1] = (byte)yMask;
            balance += pulses[band] + tell;
            updateLowband = bits > bins << Resolution;
        }

        seed = _seed;
    }

    // quant_band, decoding: the band of x (and y, for a stereo split), its collapse mask. lowband is the folding
    // source, lowbandOut where this band's shape is kept for those above.
    private uint DecodeBand(int band, Span<float> x, Span<float> y, int bins, int bits, int blocks, int change,
        Span<float> lowband, ref OpusRangeDecoder decoder, int shift, Span<float> lowbandOut, int level, float gain,
        uint fill)
    {
        var bins0 = bins;
        var binsPerBlock = bins / blocks;
        var blocks0 = blocks;
        var timeDivide = 0;
        var recombine = 0;
        var inverted = false;
        var mid = 0f;
        var side = 0f;
        var longBlocks = blocks0 == 1;
        var isStereo = !y.IsEmpty;
        var split = isStereo;
        uint mask = 0;
        var hasLowband = !lowband.IsEmpty;
        if (bins == 1)
        {
            var target = x;
            for (var channel = 0; channel < (isStereo ? 2 : 1); channel++)
            {
                var negative = false;
                if (_remaining >= 1 << Resolution)
                {
                    negative = decoder.ReadBits(1) != 0;
                    _remaining -= 1 << Resolution;
                    bits -= 1 << Resolution;
                }

                target[0] = negative ? -1f : 1f;
                target = y;
            }

            if (!lowbandOut.IsEmpty)
            {
                lowbandOut[0] = x[0];
            }

            return 1;
        }

        var lowbandCopy = lowband;
        if (!isStereo && level == 0)
        {
            if (change > 0)
            {
                recombine = change;
            }

            if (hasLowband && (recombine != 0 || ((binsPerBlock & 1) == 0 && change < 0) || blocks0 > 1))
            {
                lowband[..bins].CopyTo(_scratch);
                lowbandCopy = _scratch.AsSpan(0, bins);
            }

            for (var step = 0; step < recombine; step++)
            {
                if (hasLowband)
                {
                    Haar(lowbandCopy, bins >> step, 1 << step);
                }

                fill = BitInterleave[(int)(fill & 0xF)] | (uint)BitInterleave[(int)(fill >> 4)] << 2;
            }

            blocks >>= recombine;
            binsPerBlock <<= recombine;
            while ((binsPerBlock & 1) == 0 && change < 0)
            {
                if (hasLowband)
                {
                    Haar(lowbandCopy, binsPerBlock, blocks);
                }

                fill |= fill << blocks;
                blocks <<= 1;
                binsPerBlock >>= 1;
                timeDivide++;
                change++;
            }

            blocks0 = blocks;
            if (blocks0 > 1 && hasLowband)
            {
                Deinterleave(lowbandCopy, binsPerBlock >> recombine, blocks0 << recombine, longBlocks);
            }
        }

        var binsPerBlock0 = binsPerBlock;
        var whole = x;
        if (!isStereo && shift != -1 && bits > MostCost(band, shift) + 12 && bins > 2)
        {
            bins >>= 1;
            y = x.Slice(bins, bins);
            x = x[..bins];
            split = true;
            shift--;
            if (blocks == 1)
            {
                fill = (fill & 1) | (fill << 1);
            }

            blocks = (blocks + 1) >> 1;
        }

        if (split)
        {
            var pulseCap = CeltTables.LogWidths[band] + shift * (1 << Resolution);
            var offset = (pulseCap >> 1) - (isStereo && bins == 2 ? CeltTables.ThetaOffsetTwoPhase : CeltTables.ThetaOffset);
            var steps = StepsOf(bins, bits, offset, pulseCap, isStereo);
            if (isStereo && band >= _intensity)
            {
                steps = 1;
            }

            var tell = decoder.TellFraction();
            var theta = 0;
            if (steps != 1)
            {
                theta = DecodeTheta(ref decoder, steps, bins, isStereo, blocks0);
                theta = theta * 16384 / steps;
            }
            else if (isStereo)
            {
                inverted = bits > 2 << Resolution && _remaining > 2 << Resolution && decoder.DecodeBit(2);
                theta = 0;
            }

            var thetaBits = decoder.TellFraction() - tell;
            bits -= thetaBits;
            var originalFill = fill;
            int imid;
            int iside;
            int delta;
            if (theta == 0)
            {
                imid = 32767;
                iside = 0;
                fill &= (1u << blocks) - 1;
                delta = -16384;
            }
            else if (theta == 16384)
            {
                imid = 0;
                iside = 32767;
                fill &= ((1u << blocks) - 1) << blocks;
                delta = 16384;
            }
            else
            {
                imid = ExactCos(theta);
                iside = ExactCos(16384 - theta);
                delta = FractionMultiply((bins - 1) << 7, ExactLog2Tan(iside, imid));
            }

            mid = imid / 32768f;
            side = iside / 32768f;
            if (bins == 2 && isStereo)
            {
                var sideBits = theta != 0 && theta != 16384 ? 1 << Resolution : 0;
                var midBits = bits - sideBits;
                var isSwapped = theta > 8192;
                _remaining -= thetaBits + sideBits;
                var x2 = isSwapped ? y : x;
                var y2 = isSwapped ? x : y;
                var sign = 1;
                if (sideBits != 0)
                {
                    sign = 1 - 2 * (int)decoder.ReadBits(1);
                }

                mask = DecodeBand(band, x2, default, bins, midBits, blocks, change, lowbandCopy, ref decoder, shift,
                    lowbandOut, level, gain, originalFill);
                y2[0] = -sign * x2[1];
                y2[1] = sign * x2[0];
                x[0] *= mid;
                x[1] *= mid;
                y[0] *= side;
                y[1] *= side;
                var first = x[0];
                x[0] = first - y[0];
                y[0] = first + y[0];
                var second = x[1];
                x[1] = second - y[1];
                y[1] = second + y[1];
            }
            else
            {
                if (blocks0 > 1 && !isStereo && (theta & 0x3FFF) != 0)
                {
                    delta = theta > 8192 ? delta - (delta >> (4 - shift)) : Math.Min(0, delta + (bins << Resolution >> (5 - shift)));
                }

                var midBits = Math.Max(0, Math.Min(bits, (bits - delta) / 2));
                var sideBits = bits - midBits;
                _remaining -= thetaBits;
                var nextLowband = hasLowband && !isStereo ? lowbandCopy[bins..] : default;
                var nextLowbandOut = isStereo ? lowbandOut : default;
                var nextLevel = isStereo ? 0 : level + 1;
                var rebalance = _remaining;
                var midGain = isStereo ? 1f : gain * mid;
                var sideGain = gain * side;
                var sideShift = (int)((blocks0 >> 1) & (isStereo ? 0 : -1));
                if (midBits >= sideBits)
                {
                    mask = DecodeBand(band, x, default, bins, midBits, blocks, change, lowbandCopy, ref decoder, shift,
                        nextLowbandOut, nextLevel, midGain, fill);
                    rebalance = midBits - (rebalance - _remaining);
                    if (rebalance > 3 << Resolution && theta != 0)
                    {
                        sideBits += rebalance - (3 << Resolution);
                    }

                    mask |= DecodeBand(band, y, default, bins, sideBits, blocks, change, nextLowband, ref decoder,
                        shift, default, nextLevel, sideGain, fill >> blocks) << sideShift;
                }
                else
                {
                    mask = DecodeBand(band, y, default, bins, sideBits, blocks, change, nextLowband, ref decoder,
                        shift, default, nextLevel, sideGain, fill >> blocks) << sideShift;
                    rebalance = sideBits - (rebalance - _remaining);
                    if (rebalance > 3 << Resolution && theta != 16384)
                    {
                        midBits += rebalance - (3 << Resolution);
                    }

                    mask |= DecodeBand(band, x, default, bins, midBits, blocks, change, lowbandCopy, ref decoder,
                        shift, nextLowbandOut, nextLevel, midGain, fill);
                }
            }
        }
        else
        {
            var pseudo = CeltAllocation.PulsesOf(band, shift, bits);
            var cost = CeltAllocation.BitsOf(band, shift, pseudo);
            _remaining -= cost;
            while (_remaining < 0 && pseudo > 0)
            {
                _remaining += cost;
                pseudo--;
                cost = CeltAllocation.BitsOf(band, shift, pseudo);
                _remaining -= cost;
            }

            if (pseudo != 0)
            {
                mask = DecodeVector(x[..bins], CeltAllocation.RealPulses(pseudo), blocks, ref decoder, gain);
            }
            else
            {
                var all = (1u << blocks) - 1;
                fill &= all;
                if (fill == 0)
                {
                    x[..bins].Clear();
                }
                else
                {
                    if (!hasLowband)
                    {
                        for (var at = 0; at < bins; at++)
                        {
                            _seed = NextRandom(_seed);
                            x[at] = (int)_seed >> 20;
                        }

                        mask = all;
                    }
                    else
                    {
                        for (var at = 0; at < bins; at++)
                        {
                            _seed = NextRandom(_seed);
                            x[at] = lowbandCopy[at] + ((_seed & 0x8000) != 0 ? 1f / 256 : -1f / 256);
                        }

                        mask = fill;
                    }

                    Renormalize(x[..bins], gain);
                }
            }
        }

        if (isStereo)
        {
            if (bins != 2)
            {
                StereoMerge(x[..bins], y[..bins], mid);
            }

            if (inverted)
            {
                for (var at = 0; at < bins; at++)
                {
                    y[at] = -y[at];
                }
            }
        }
        else if (level == 0)
        {
            x = whole;
            if (blocks0 > 1)
            {
                Interleave(x, binsPerBlock >> recombine, blocks0 << recombine, longBlocks);
            }

            binsPerBlock = binsPerBlock0;
            blocks = blocks0;
            for (var step = 0; step < timeDivide; step++)
            {
                blocks >>= 1;
                binsPerBlock <<= 1;
                mask |= mask >> blocks;
                Haar(x, binsPerBlock, blocks);
            }

            for (var step = 0; step < recombine; step++)
            {
                mask = BitDeinterleave[(int)mask];
                Haar(x, bins0 >> step, 1 << step);
            }

            blocks <<= recombine;
            if (!lowbandOut.IsEmpty)
            {
                var scale = MathF.Sqrt(bins0);
                for (var at = 0; at < bins0; at++)
                {
                    lowbandOut[at] = scale * x[at];
                }
            }

            mask &= (1u << blocks) - 1;
        }

        return mask;
    }

    // The cost of a band's most pulses at the shift, in eighths less one.
    private static int MostCost(int band, int shift)
    {
        var costs = CeltTables.PulseCosts[CeltTables.PulseCostStarts[(shift + 1) * CeltTables.Bands + band]..];
        return costs[costs[0]];
    }

    private static int DecodeTheta(ref OpusRangeDecoder decoder, int steps, int bins, bool isStereo, int blocks0)
    {
        if (isStereo && bins > 2)
        {
            const int step = 3;
            var half = steps / 2;
            var total = (uint)(step * (half + 1) + half);
            var value = (int)decoder.Decode(total);
            var theta = value < (half + 1) * step ? value / step : half + 1 + (value - (half + 1) * step);
            var low = theta <= half ? step * theta : theta - 1 - half + (half + 1) * step;
            var high = theta <= half ? step * (theta + 1) : theta - half + (half + 1) * step;
            decoder.Update((uint)low, (uint)high, total);
            return theta;
        }

        if (blocks0 > 1 || isStereo)
        {
            return (int)decoder.DecodeUniform((uint)steps + 1);
        }

        // A triangular distribution.
        var top = steps >> 1;
        var all = (top + 1) * (top + 1);
        var found = (int)decoder.Decode((uint)all);
        int result;
        int size;
        int start;
        if (found < (top * (top + 1) >> 1))
        {
            result = ((int)SquareRoot(8 * (uint)found + 1) - 1) >> 1;
            size = result + 1;
            start = result * (result + 1) >> 1;
        }
        else
        {
            result = (2 * (steps + 1) - (int)SquareRoot(8 * (uint)(all - found - 1) + 1)) >> 1;
            size = steps + 1 - result;
            start = all - ((steps + 1 - result) * (steps + 2 - result) >> 1);
        }

        decoder.Update((uint)start, (uint)(start + size), (uint)all);
        return result;
    }

    // compute_qn: how finely the angle is coded.
    private static int StepsOf(int bins, int bits, int offset, int pulseCap, bool isStereo)
    {
        ReadOnlySpan<short> exp2 = [16384, 17866, 19483, 21247, 23170, 25267, 27554, 30048];
        var width = 2 * bins - 1;
        if (isStereo && bins == 2)
        {
            width--;
        }

        var quality = Math.Min(bits - pulseCap - (4 << Resolution), (bits + width * offset) / width);
        quality = Math.Min(8 << Resolution, quality);
        if (quality < (1 << Resolution >> 1))
        {
            return 1;
        }

        var steps = exp2[quality & 7] >> (14 - (quality >> Resolution));
        return (steps + 1) >> 1 << 1;
    }

    // alg_unquant: the pulses, normalized, rotated back, and which blocks have any.
    private uint DecodeVector(Span<float> x, int pulses, int blocks, ref OpusRangeDecoder decoder, float gain)
    {
        var vector = _pulses.AsSpan(0, x.Length);
        CeltPulses.Decode(vector, pulses, ref decoder);
        var energy = 0f;
        foreach (var value in vector)
        {
            energy += value * value;
        }

        var scale = gain / MathF.Sqrt(energy);
        for (var at = 0; at < x.Length; at++)
        {
            x[at] = scale * vector[at];
        }

        Rotate(x, -1, blocks, pulses, _spread);
        if (blocks <= 1)
        {
            return 1;
        }

        var perBlock = x.Length / blocks;
        uint mask = 0;
        for (var block = 0; block < blocks; block++)
        {
            for (var at = 0; at < perBlock; at++)
            {
                mask |= (vector[block * perBlock + at] != 0 ? 1u : 0) << block;
            }
        }

        return mask;
    }

    // exp_rotation: the spread, as rotations of neighbours.
    private static void Rotate(Span<float> x, int direction, int stride, int pulses, int spread)
    {
        ReadOnlySpan<int> factors = [15, 10, 5];
        var length = x.Length;
        if (2 * pulses >= length || spread == SpreadNone)
        {
            return;
        }

        var factor = factors[spread - 1];
        var gain = (float)length / (length + factor * pulses);
        var theta = .5f * gain * gain;
        var cos = MathF.Cos(.5f * MathF.PI * theta);
        var sin = MathF.Cos(.5f * MathF.PI * (1 - theta));
        var stride2 = 0;
        if (length >= 8 * stride)
        {
            stride2 = 1;
            while ((stride2 * stride2 + stride2) * stride + (stride >> 2) < length)
            {
                stride2++;
            }
        }

        length /= stride;
        for (var block = 0; block < stride; block++)
        {
            var part = x.Slice(block * length, length);
            if (direction < 0)
            {
                if (stride2 != 0)
                {
                    RotatePairs(part, stride2, sin, cos);
                }

                RotatePairs(part, 1, cos, sin);
            }
            else
            {
                RotatePairs(part, 1, cos, -sin);
                if (stride2 != 0)
                {
                    RotatePairs(part, stride2, sin, -cos);
                }
            }
        }
    }

    private static void RotatePairs(Span<float> x, int stride, float cos, float sin)
    {
        for (var at = 0; at < x.Length - stride; at++)
        {
            var first = x[at];
            var second = x[at + stride];
            x[at + stride] = cos * second + sin * first;
            x[at] = cos * first - sin * second;
        }

        for (var at = x.Length - 2 * stride - 1; at >= 0; at--)
        {
            var first = x[at];
            var second = x[at + stride];
            x[at + stride] = cos * second + sin * first;
            x[at] = cos * first - sin * second;
        }
    }

    private static void StereoMerge(Span<float> x, Span<float> y, float mid)
    {
        var cross = 0f;
        var side = 0f;
        for (var at = 0; at < x.Length; at++)
        {
            cross += x[at] * y[at];
            side += y[at] * y[at];
        }

        cross *= mid;
        var mid2 = mid;
        var left = mid2 * mid2 + side - 2 * cross;
        var right = mid2 * mid2 + side + 2 * cross;
        if (right < 6e-4f || left < 6e-4f)
        {
            x.CopyTo(y);
            return;
        }

        var leftGain = 1 / MathF.Sqrt(left);
        var rightGain = 1 / MathF.Sqrt(right);
        for (var at = 0; at < x.Length; at++)
        {
            var l = mid * x[at];
            var r = y[at];
            x[at] = leftGain * (l - r);
            y[at] = rightGain * (l + r);
        }
    }

    /// <summary>renormalise_vector: to an energy of <paramref name="gain"/> squared.</summary>
    public static void Renormalize(Span<float> x, float gain)
    {
        var energy = 1e-15f;
        foreach (var value in x)
        {
            energy += value * value;
        }

        var scale = gain / MathF.Sqrt(energy);
        for (var at = 0; at < x.Length; at++)
        {
            x[at] *= scale;
        }
    }

    private static void Haar(Span<float> x, int bins, int stride)
    {
        bins >>= 1;
        for (var column = 0; column < stride; column++)
        {
            for (var at = 0; at < bins; at++)
            {
                var first = .70710678f * x[stride * 2 * at + column];
                var second = .70710678f * x[stride * (2 * at + 1) + column];
                x[stride * 2 * at + column] = first + second;
                x[stride * (2 * at + 1) + column] = first - second;
            }
        }
    }

    private void Deinterleave(Span<float> x, int bins, int stride, bool hadamard)
    {
        var all = bins * stride;
        var temporary = _hadamard.AsSpan(0, all);
        var order = CeltTables.HadamardOrder[(stride - 2)..];
        for (var column = 0; column < stride; column++)
        {
            var to = hadamard ? order[column] : column;
            for (var at = 0; at < bins; at++)
            {
                temporary[to * bins + at] = x[at * stride + column];
            }
        }

        temporary.CopyTo(x);
    }

    private void Interleave(Span<float> x, int bins, int stride, bool hadamard)
    {
        var all = bins * stride;
        var temporary = _hadamard.AsSpan(0, all);
        var order = CeltTables.HadamardOrder[(stride - 2)..];
        for (var column = 0; column < stride; column++)
        {
            var from = hadamard ? order[column] : column;
            for (var at = 0; at < bins; at++)
            {
                temporary[at * stride + column] = x[from * bins + at];
            }
        }

        temporary.CopyTo(x);
    }

    public static uint NextRandom(uint seed) => 1664525 * seed + 1013904223;

    private static int FractionMultiply(int a, int b) => (16384 + (short)a * (short)b) >> 15;

    // bitexact_cos: cos(pi/2 x / 16384) in Q15, as the encoder has it to the bit.
    private static int ExactCos(int theta)
    {
        var square = (4096 + theta * theta) >> 13;
        var value = (short)square;
        value = (short)(32767 - value + FractionMultiply(value, -7651 + FractionMultiply(value, 8277 + FractionMultiply(-626, value))));
        return 1 + value;
    }

    // bitexact_log2tan.
    private static int ExactLog2Tan(int sin, int cos)
    {
        var logCos = 32 - int.CreateTruncating(uint.LeadingZeroCount((uint)cos));
        var logSin = 32 - int.CreateTruncating(uint.LeadingZeroCount((uint)sin));
        cos <<= 15 - logCos;
        sin <<= 15 - logSin;
        return (logSin - logCos) * (1 << 11) + FractionMultiply(sin, FractionMultiply(sin, -2597) + 7932) -
            FractionMultiply(cos, FractionMultiply(cos, -2597) + 7932);
    }

    // isqrt32.
    private static uint SquareRoot(uint value)
    {
        uint root = 0;
        var shift = (32 - int.CreateTruncating(uint.LeadingZeroCount(value)) - 1) >> 1;
        var bit = 1u << shift;
        do
        {
            var trial = ((root << 1) + bit) << shift;
            if (trial <= value)
            {
                root += bit;
                value -= trial;
            }

            bit >>= 1;
            shift--;
        }
        while (shift >= 0);

        return root;
    }

    private static ReadOnlySpan<byte> BitInterleave => [0, 1, 1, 1, 2, 3, 3, 3, 2, 3, 3, 3, 2, 3, 3, 3];

    private static ReadOnlySpan<byte> BitDeinterleave =>
        [0x00, 0x03, 0x0C, 0x0F, 0x30, 0x33, 0x3C, 0x3F, 0xC0, 0xC3, 0xCC, 0xCF, 0xF0, 0xF3, 0xFC, 0xFF];
}
