using static Adamantium.Pulsar.Decoding.SilkMath;

namespace Adamantium.Pulsar.Decoding;

/// <summary>One channel of SILK (RFC 6716, 4.2.7): a frame's indices read - type, gains, the LSFs, pitch and LTP
/// filters, the seed and the excitation's pulses - then turned into its parameters, and the excitation through the
/// long-term and the LPC synthesis filters into samples at SILK's rate. What the next frame predicts from, it keeps.
/// </summary>
internal sealed class SilkChannel
{
    public const int Voiced = 2;
    public const int Independent = 0;
    public const int IndependentNoLtpScaling = 1;
    public const int Conditional = 2;
    private const int MostOrder = 16;
    private const int LtpOrder = 5;
    private const int MostFrame = 320;
    private const int MostSubframe = 80;
    private const int ShellLength = 16;
    private const int MostPulses = 16;
    private const int QuantizationAdjust = 80;

    private readonly int[] _gainIndices = new int[4];
    private readonly int[] _nlsfIndices = new int[MostOrder + 1];
    private readonly int[] _ltpIndices = new int[4];
    private readonly short[] _previousNlsf = new short[MostOrder];
    private readonly int[] _excitation = new int[MostFrame];
    private readonly int[] _lpcState = new int[MostOrder];
    private readonly short[] _history = new short[MostFrame + 2 * MostSubframe];
    private readonly int[] _pulses = new int[MostFrame];
    private readonly short[] _coefficients = new short[2 * MostOrder];
    private readonly int[] _gains = new int[4];
    private readonly int[] _pitchLags = new int[4];
    private readonly short[] _ltpFilters = new short[4 * LtpOrder];
    private readonly short[] _ltpSignal = new short[MostFrame];
    private readonly int[] _ltpState = new int[2 * MostFrame];
    private readonly int[] _residual = new int[MostSubframe];
    private readonly int[] _lpc = new int[MostSubframe + MostOrder];
    private int _signalType;
    private int _offsetType;
    private int _interpolation;
    private int _lagIndex;
    private int _contourIndex;
    private int _periodicity;
    private int _ltpScaleIndex;
    private int _seed;
    private int _previousTypeCoded;
    private int _previousLagIndex;
    private int _lastGainIndex = 10;
    private int _previousGain = 65536;
    private int _ltpScale;
    private bool _isFirstAfterReset = true;
    private int _order;
    private int _historyLength;
    private bool _isWide;

    public SilkChannel() => Reset();

    public SilkResampler Resampler { get; } = new();

    /// <summary>In kHz: 8, 12 or 16; 0 until the first frame.</summary>
    public int Rate { get; private set; }

    public int Subframes { get; private set; } = 4;

    public int SubframeLength { get; private set; }

    public int FrameLength { get; private set; }

    public int FramesPerPacket { get; set; }

    public int FramesDecoded { get; set; }

    public bool[] ActiveFlags { get; } = new bool[3];

    public bool HasRedundancy { get; set; }

    public bool[] RedundantFlags { get; } = new bool[3];

    public int LagPrevious { get; set; } = 100;

    public int PreviousSignalType { get; private set; }

    public int LossCount { get; set; }

    /// <summary>silk_init_decoder.</summary>
    public void Reset()
    {
        Rate = 0;
        FrameLength = 0;
        Subframes = 4;
        _isFirstAfterReset = true;
        _previousGain = 65536;
        _lastGainIndex = 0;
        LagPrevious = 0;
        PreviousSignalType = 0;
        _previousTypeCoded = 0;
        _previousLagIndex = 0;
        LossCount = 0;
        FramesDecoded = 0;
        FramesPerPacket = 0;
        HasRedundancy = false;
        Array.Clear(_previousNlsf);
        Array.Clear(_lpcState);
        Array.Clear(_history);
        Array.Clear(_excitation);
        Array.Clear(ActiveFlags);
        Array.Clear(RedundantFlags);
    }

    /// <summary>What a side channel decoded again after mid only starts from.</summary>
    public void ResetSide()
    {
        Array.Clear(_history);
        Array.Clear(_lpcState);
        LagPrevious = 100;
        _lastGainIndex = 10;
        PreviousSignalType = 0;
        _isFirstAfterReset = true;
    }

    /// <summary>After a frame lost: the gains start over.</summary>
    public void ForgetGain() => _lastGainIndex = 10;

    /// <summary>silk_decoder_set_fs.</summary>
    public void SetRate(int rate, int subframes)
    {
        Subframes = subframes;
        SubframeLength = 5 * rate;
        var frameLength = subframes * SubframeLength;
        if (Rate != rate)
        {
            Resampler.Reset(rate * 1000);
        }

        if (Rate != rate || frameLength != FrameLength)
        {
            if (Rate != rate)
            {
                _historyLength = 20 * rate;
                _order = rate == 16 ? 16 : 10;
                _isWide = rate == 16;
                _isFirstAfterReset = true;
                LagPrevious = 100;
                _lastGainIndex = 10;
                PreviousSignalType = 0;
                Array.Clear(_history);
                Array.Clear(_lpcState);
            }

            Rate = rate;
            FrameLength = frameLength;
        }
    }

    /// <summary>A frame's indices and pulses read and passed by: an LBRR frame, not played.</summary>
    public void Skip(ref OpusRangeDecoder decoder, int frame, int coding)
    {
        DecodeIndices(ref decoder, frame, true, coding);
        DecodePulses(ref decoder);
    }

    /// <summary>silk_decode_frame: a regular frame into <paramref name="output"/>, or silence for one lost.</summary>
    public void Decode(ref OpusRangeDecoder decoder, Span<short> output, bool isLost, int coding)
    {
        var length = FrameLength;
        if (!isLost)
        {
            DecodeIndices(ref decoder, FramesDecoded, false, coding);
            DecodePulses(ref decoder);
            DecodeParameters(coding);
            Synthesize(output[..length]);
            LossCount = 0;
            PreviousSignalType = _signalType;
            _isFirstAfterReset = false;
            LagPrevious = _pitchLags[Subframes - 1];
        }
        else
        {
            // Concealed by silence; what comes next is bandwidth expanded as after any loss.
            output[..length].Clear();
            LossCount++;
        }

        var kept = _historyLength - length;
        Array.Copy(_history, length, _history, 0, kept);
        output[..length].CopyTo(_history.AsSpan(kept));
    }

    // silk_decode_indices.
    private void DecodeIndices(ref OpusRangeDecoder decoder, int frame, bool isRedundant, int coding)
    {
        var type = isRedundant || ActiveFlags[frame]
            ? decoder.DecodeInverse(SilkTables.TypeActiveInverse, 8) + 2
            : decoder.DecodeInverse(SilkTables.TypeInactiveInverse, 8);
        _signalType = type >> 1;
        _offsetType = type & 1;
        if (coding == Conditional)
        {
            _gainIndices[0] = decoder.DecodeInverse(SilkTables.DeltaGainInverse, 8);
        }
        else
        {
            _gainIndices[0] = decoder.DecodeInverse(SilkTables.GainInverse.Slice(_signalType * 8, 8), 8) << 3;
            _gainIndices[0] += decoder.DecodeInverse(SilkTables.Uniform8, 8);
        }

        for (var subframe = 1; subframe < Subframes; subframe++)
        {
            _gainIndices[subframe] = decoder.DecodeInverse(SilkTables.DeltaGainInverse, 8);
        }

        var vectors = 32;
        var stage1 = _isWide ? SilkTables.WideStage1Inverse : SilkTables.NarrowStage1Inverse;
        _nlsfIndices[0] = decoder.DecodeInverse(stage1[((_signalType >> 1) * vectors)..], 8);
        Span<int> entries = stackalloc int[MostOrder];
        Span<int> predictions = stackalloc int[MostOrder];
        Unpack(entries, predictions, _nlsfIndices[0]);
        var stage2 = _isWide ? SilkTables.WideStage2Inverse : SilkTables.NarrowStage2Inverse;
        for (var at = 0; at < _order; at++)
        {
            var index = decoder.DecodeInverse(stage2[entries[at]..], 8);
            if (index == 0)
            {
                index -= decoder.DecodeInverse(SilkTables.NlsfExtensionInverse, 8);
            }
            else if (index == 8)
            {
                index += decoder.DecodeInverse(SilkTables.NlsfExtensionInverse, 8);
            }

            _nlsfIndices[at + 1] = index - 4;
        }

        _interpolation = Subframes == 4 ? decoder.DecodeInverse(SilkTables.InterpolationInverse, 8) : 4;
        if (_signalType == Voiced)
        {
            var isAbsolute = true;
            if (coding == Conditional && _previousTypeCoded == Voiced)
            {
                var delta = decoder.DecodeInverse(SilkTables.PitchDeltaInverse, 8);
                if (delta > 0)
                {
                    _lagIndex = _previousLagIndex + delta - 9;
                    isAbsolute = false;
                }
            }

            if (isAbsolute)
            {
                _lagIndex = decoder.DecodeInverse(SilkTables.PitchLagInverse, 8) * (Rate >> 1);
                var lowBits = Rate switch { 16 => SilkTables.Uniform8, 12 => SilkTables.Uniform6, _ => SilkTables.Uniform4 };
                _lagIndex += decoder.DecodeInverse(lowBits, 8);
            }

            _previousLagIndex = _lagIndex;
            var contour = Rate == 8
                ? Subframes == 4 ? SilkTables.PitchContourNarrowInverse : SilkTables.PitchContour10NarrowInverse
                : Subframes == 4 ? SilkTables.PitchContourInverse : SilkTables.PitchContour10Inverse;
            _contourIndex = decoder.DecodeInverse(contour, 8);
            _periodicity = decoder.DecodeInverse(SilkTables.PeriodicityInverse, 8);
            var gains = _periodicity switch
            {
                0 => SilkTables.LtpGainInverse0,
                1 => SilkTables.LtpGainInverse1,
                _ => SilkTables.LtpGainInverse2,
            };
            for (var subframe = 0; subframe < Subframes; subframe++)
            {
                _ltpIndices[subframe] = decoder.DecodeInverse(gains, 8);
            }

            _ltpScaleIndex = coding == Independent ? decoder.DecodeInverse(SilkTables.LtpScaleInverse, 8) : 0;
        }

        _previousTypeCoded = _signalType;
        _seed = decoder.DecodeInverse(SilkTables.Uniform4, 8);
    }

    // silk_NLSF_unpack: by coefficient, where its stage-2 table begins, and its prediction weight.
    private void Unpack(Span<int> entries, Span<int> predictions, int index)
    {
        var select = _isWide ? SilkTables.WideSelect : SilkTables.NarrowSelect;
        var prediction = _isWide ? SilkTables.WidePrediction : SilkTables.NarrowPrediction;
        var selection = select[(index * _order / 2)..];
        for (var at = 0; at < _order; at += 2)
        {
            var entry = selection[at / 2];
            entries[at] = ((entry >> 1) & 7) * 9;
            predictions[at] = prediction[at + (entry & 1) * (_order - 1)];
            entries[at + 1] = ((entry >> 5) & 7) * 9;
            predictions[at + 1] = prediction[at + ((entry >> 4) & 1) * (_order - 1) + 1];
        }
    }

    // silk_decode_pulses: by shell blocks of 16, their pulse counts, their places by halves, their low bits and signs.
    private void DecodePulses(ref OpusRangeDecoder decoder)
    {
        var length = FrameLength;
        var rateLevel = decoder.DecodeInverse(SilkTables.RateLevelInverse.Slice((_signalType >> 1) * 9, 9), 8);
        var blocks = length >> 4;
        if (blocks * ShellLength < length)
        {
            blocks++;
        }

        Span<int> sums = stackalloc int[MostFrame / ShellLength + 1];
        Span<int> shifts = stackalloc int[MostFrame / ShellLength + 1];
        var perBlock = SilkTables.PulsesPerBlockInverse;
        for (var block = 0; block < blocks; block++)
        {
            shifts[block] = 0;
            sums[block] = decoder.DecodeInverse(perBlock.Slice(rateLevel * 18, 18), 8);
            while (sums[block] == MostPulses + 1)
            {
                shifts[block]++;
                sums[block] = decoder.DecodeInverse(perBlock[(9 * 18 + (shifts[block] == 10 ? 1 : 0))..], 8);
            }
        }

        var pulses = _pulses.AsSpan(0, blocks * ShellLength);
        for (var block = 0; block < blocks; block++)
        {
            var part = pulses.Slice(block * ShellLength, ShellLength);
            if (sums[block] > 0)
            {
                DecodeShell(part, ref decoder, sums[block]);
            }
            else
            {
                part.Clear();
            }
        }

        for (var block = 0; block < blocks; block++)
        {
            if (shifts[block] > 0)
            {
                var part = pulses.Slice(block * ShellLength, ShellLength);
                for (var at = 0; at < ShellLength; at++)
                {
                    var value = part[at];
                    for (var bit = 0; bit < shifts[block]; bit++)
                    {
                        value = (value << 1) + decoder.DecodeInverse(SilkTables.LsbInverse, 8);
                    }

                    part[at] = value;
                }

                sums[block] |= shifts[block] << 5;
            }
        }

        // silk_decode_signs.
        Span<byte> inverse = stackalloc byte[2];
        var signs = SilkTables.SignInverse[(7 * (_offsetType + (_signalType << 1)))..];
        var signBlocks = (length + ShellLength / 2) >> 4;
        for (var block = 0; block < signBlocks; block++)
        {
            var sum = sums[block];
            if (sum > 0)
            {
                inverse[0] = signs[Math.Min(sum & 0x1F, 6)];
                var part = pulses.Slice(block * ShellLength, ShellLength);
                for (var at = 0; at < ShellLength; at++)
                {
                    if (part[at] > 0)
                    {
                        part[at] *= (decoder.DecodeInverse(inverse, 8) << 1) - 1;
                    }
                }
            }
        }
    }

    // silk_shell_decoder: 16 pulses split in halves, 4 levels deep.
    private static void DecodeShell(Span<int> pulses, ref OpusRangeDecoder decoder, int total)
    {
        Span<int> level3 = stackalloc int[2];
        Span<int> level2 = stackalloc int[4];
        Span<int> level1 = stackalloc int[8];
        Split(level3, 0, ref decoder, total, SilkTables.Shell3);
        Split(level2, 0, ref decoder, level3[0], SilkTables.Shell2);
        Split(level1, 0, ref decoder, level2[0], SilkTables.Shell1);
        Split(pulses, 0, ref decoder, level1[0], SilkTables.Shell0);
        Split(pulses, 2, ref decoder, level1[1], SilkTables.Shell0);
        Split(level1, 2, ref decoder, level2[1], SilkTables.Shell1);
        Split(pulses, 4, ref decoder, level1[2], SilkTables.Shell0);
        Split(pulses, 6, ref decoder, level1[3], SilkTables.Shell0);
        Split(level2, 2, ref decoder, level3[1], SilkTables.Shell2);
        Split(level1, 4, ref decoder, level2[2], SilkTables.Shell1);
        Split(pulses, 8, ref decoder, level1[4], SilkTables.Shell0);
        Split(pulses, 10, ref decoder, level1[5], SilkTables.Shell0);
        Split(level1, 6, ref decoder, level2[3], SilkTables.Shell1);
        Split(pulses, 12, ref decoder, level1[6], SilkTables.Shell0);
        Split(pulses, 14, ref decoder, level1[7], SilkTables.Shell0);
    }

    private static void Split(scoped Span<int> children, int at, ref OpusRangeDecoder decoder, int total,
        ReadOnlySpan<byte> table)
    {
        if (total > 0)
        {
            children[at] = decoder.DecodeInverse(table[SilkTables.ShellOffsets[total]..], 8);
            children[at + 1] = total - children[at];
        }
        else
        {
            children[at] = children[at + 1] = 0;
        }
    }

    // silk_decode_parameters.
    private void DecodeParameters(int coding)
    {
        DequantizeGains(coding == Conditional);
        Span<short> nlsf = stackalloc short[MostOrder];
        Span<short> interpolated = stackalloc short[MostOrder];
        DecodeNlsf(nlsf[.._order]);
        var current = _coefficients.AsSpan(MostOrder, _order);
        var first = _coefficients.AsSpan(0, _order);
        NlsfToLpc(current, nlsf[.._order]);
        if (_isFirstAfterReset)
        {
            _interpolation = 4;
        }

        if (_interpolation < 4)
        {
            for (var at = 0; at < _order; at++)
            {
                interpolated[at] = (short)(_previousNlsf[at] + (_interpolation * (nlsf[at] - _previousNlsf[at]) >> 2));
            }

            NlsfToLpc(first, interpolated[.._order]);
        }
        else
        {
            current.CopyTo(first);
        }

        nlsf[.._order].CopyTo(_previousNlsf);
        if (LossCount != 0)
        {
            ExpandBandwidth(first, 63570);
            ExpandBandwidth(current, 63570);
        }

        if (_signalType == Voiced)
        {
            DecodePitch();
            var filters = _periodicity switch
            {
                0 => SilkTables.LtpFilters0,
                1 => SilkTables.LtpFilters1,
                _ => SilkTables.LtpFilters2,
            };
            for (var subframe = 0; subframe < Subframes; subframe++)
            {
                for (var tap = 0; tap < LtpOrder; tap++)
                {
                    _ltpFilters[subframe * LtpOrder + tap] = (short)(filters[_ltpIndices[subframe] * LtpOrder + tap] << 7);
                }
            }

            _ltpScale = SilkTables.LtpScales[_ltpScaleIndex];
        }
        else
        {
            Array.Clear(_pitchLags);
            Array.Clear(_ltpFilters);
            _periodicity = 0;
            _ltpScale = 0;
        }
    }

    // silk_gains_dequant.
    private void DequantizeGains(bool isConditional)
    {
        const int levels = 64;
        const int offset = 2 * 128 / 6 + 16 * 128;
        const int inverseScale = 65536 * ((88 - 2) * 128 / 6) / (levels - 1);
        for (var subframe = 0; subframe < Subframes; subframe++)
        {
            if (subframe == 0 && !isConditional)
            {
                _lastGainIndex = Math.Max(_gainIndices[subframe], _lastGainIndex - 16);
            }
            else
            {
                var index = _gainIndices[subframe] - 4;
                var threshold = 2 * 36 - levels + _lastGainIndex;
                _lastGainIndex += index > threshold ? (index << 1) - threshold : index;
            }

            _lastGainIndex = Math.Clamp(_lastGainIndex, 0, levels - 1);
            _gains[subframe] = Log2ToLinear(Math.Min(MulWB(inverseScale, _lastGainIndex) + offset, 3967));
        }
    }

    // silk_NLSF_decode: stage 1's vector, stage 2's residuals predicted backwards and weighted, then stabilized.
    private void DecodeNlsf(Span<short> nlsf)
    {
        var order = _order;
        var codebook = (_isWide ? SilkTables.WideCodebook : SilkTables.NarrowCodebook).Slice(_nlsfIndices[0] * order, order);
        for (var at = 0; at < order; at++)
        {
            nlsf[at] = (short)(codebook[at] << 7);
        }

        Span<int> entries = stackalloc int[MostOrder];
        Span<int> predictions = stackalloc int[MostOrder];
        Unpack(entries, predictions, _nlsfIndices[0]);
        Span<short> residuals = stackalloc short[MostOrder];
        var step = _isWide ? (int)(.15 * 65536 + .5) : (int)(.18 * 65536 + .5);
        var output = 0;
        for (var at = order - 1; at >= 0; at--)
        {
            var predicted = MulBB(output, (short)predictions[at]) >> 8;
            output = _nlsfIndices[at + 1] << 10;
            if (output > 0)
            {
                output -= 102;
            }
            else if (output < 0)
            {
                output += 102;
            }

            output = MulAddWB(predicted, output, step);
            residuals[at] = (short)output;
        }

        Span<short> weights = stackalloc short[MostOrder];
        LaroiaWeights(weights[..order], nlsf);
        for (var at = 0; at < order; at++)
        {
            var weight = SquareRootApproximation(weights[at] << 16);
            var value = nlsf[at] + (residuals[at] << 14) / weight;
            nlsf[at] = (short)Math.Clamp(value, 0, 32767);
        }

        Stabilize(nlsf, _isWide ? SilkTables.WideLeastSpacing : SilkTables.NarrowLeastSpacing);
    }

    // silk_NLSF_VQ_weights_laroia, in Q2.
    private static void LaroiaWeights(Span<short> weights, ReadOnlySpan<short> nlsf)
    {
        var order = weights.Length;
        var first = (1 << 17) / Math.Max((int)nlsf[0], 1);
        var second = (1 << 17) / Math.Max(nlsf[1] - nlsf[0], 1);
        weights[0] = (short)Math.Min(first + second, short.MaxValue);
        for (var at = 1; at < order - 1; at += 2)
        {
            first = (1 << 17) / Math.Max(nlsf[at + 1] - nlsf[at], 1);
            weights[at] = (short)Math.Min(first + second, short.MaxValue);
            second = (1 << 17) / Math.Max(nlsf[at + 2] - nlsf[at + 1], 1);
            weights[at + 1] = (short)Math.Min(first + second, short.MaxValue);
        }

        first = (1 << 17) / Math.Max((1 << 15) - nlsf[order - 1], 1);
        weights[order - 1] = (short)Math.Min(first + second, short.MaxValue);
    }

    // silk_NLSF_stabilize: each at least its spacing from the one below, by moving the closest pair apart.
    private static void Stabilize(Span<short> nlsf, ReadOnlySpan<short> spacing)
    {
        var order = nlsf.Length;
        for (var loop = 0; loop < 20; loop++)
        {
            var least = nlsf[0] - spacing[0];
            var worst = 0;
            for (var at = 1; at < order; at++)
            {
                var difference = nlsf[at] - (nlsf[at - 1] + spacing[at]);
                if (difference < least)
                {
                    least = difference;
                    worst = at;
                }
            }

            var top = (1 << 15) - (nlsf[order - 1] + spacing[order]);
            if (top < least)
            {
                least = top;
                worst = order;
            }

            if (least >= 0)
            {
                return;
            }

            if (worst == 0)
            {
                nlsf[0] = spacing[0];
            }
            else if (worst == order)
            {
                nlsf[order - 1] = (short)((1 << 15) - spacing[order]);
            }
            else
            {
                var lowest = 0;
                for (var at = 0; at < worst; at++)
                {
                    lowest += spacing[at];
                }

                lowest += spacing[worst] >> 1;
                var highest = 1 << 15;
                for (var at = order; at > worst; at--)
                {
                    highest -= spacing[at];
                }

                highest -= spacing[worst] >> 1;
                var center = (short)Limit(ShiftRound(nlsf[worst - 1] + nlsf[worst], 1), lowest, highest);
                nlsf[worst - 1] = (short)(center - (spacing[worst] >> 1));
                nlsf[worst] = (short)(nlsf[worst - 1] + spacing[worst]);
            }
        }

        nlsf.Sort();
        nlsf[0] = (short)Math.Max(nlsf[0], spacing[0]);
        for (var at = 1; at < order; at++)
        {
            // RFC 8251, 7: saturated, not wrapped.
            nlsf[at] = (short)Math.Max(nlsf[at], Saturate16(nlsf[at - 1] + spacing[at]));
        }

        nlsf[order - 1] = (short)Math.Min(nlsf[order - 1], (1 << 15) - spacing[order]);
        for (var at = order - 2; at >= 0; at--)
        {
            nlsf[at] = (short)Math.Min(nlsf[at], nlsf[at + 1] - spacing[at + 1]);
        }
    }

    // silk_LIMIT: either order of bounds.
    private static int Limit(int value, int first, int second) =>
        first > second ? Math.Clamp(value, second, first) : Math.Clamp(value, first, second);

    // silk_NLSF2A: the LSFs' cosines, the two polynomials, their sum and difference; then the coefficients kept in
    // range and the filter stable.
    private static void NlsfToLpc(Span<short> lpc, ReadOnlySpan<short> nlsf)
    {
        const int q = 16;
        ReadOnlySpan<byte> ordering16 = [0, 15, 8, 7, 4, 11, 12, 3, 2, 13, 10, 5, 6, 9, 14, 1];
        ReadOnlySpan<byte> ordering10 = [0, 9, 6, 3, 4, 5, 8, 1, 2, 7];
        var order = nlsf.Length;
        var ordering = order == 16 ? ordering16 : ordering10;
        Span<int> cosines = stackalloc int[MostOrder];
        for (var at = 0; at < order; at++)
        {
            var whole = nlsf[at] >> 8;
            var fraction = nlsf[at] - (whole << 8);
            var cosine = SilkTables.CosineQ12[whole];
            var delta = SilkTables.CosineQ12[whole + 1] - cosine;
            cosines[ordering[at]] = ShiftRound((cosine << 8) + delta * fraction, 20 - q);
        }

        var half = order >> 1;
        Span<int> p = stackalloc int[MostOrder / 2 + 1];
        Span<int> qPolynomial = stackalloc int[MostOrder / 2 + 1];
        FindPolynomial(p, cosines, 0, half);
        FindPolynomial(qPolynomial, cosines, 1, half);
        Span<int> wide = stackalloc int[MostOrder];
        for (var at = 0; at < half; at++)
        {
            var sum = p[at + 1] + p[at];
            var difference = qPolynomial[at + 1] - qPolynomial[at];
            wide[at] = -difference - sum;
            wide[order - at - 1] = difference - sum;
        }

        var round = 0;
        for (; round < 10; round++)
        {
            var most = 0;
            var index = 0;
            for (var at = 0; at < order; at++)
            {
                var value = Math.Abs(wide[at]);
                if (value > most)
                {
                    most = value;
                    index = at;
                }
            }

            most = ShiftRound(most, q + 1 - 12);
            if (most <= short.MaxValue)
            {
                break;
            }

            most = Math.Min(most, 163838);
            var chirp = (int)(.999 * 65536 + .5) - ((most - short.MaxValue) << 14) / ((most * (index + 1)) >> 2);
            ExpandBandwidth(wide[..order], chirp);
        }

        if (round == 10)
        {
            for (var at = 0; at < order; at++)
            {
                lpc[at] = Saturate16(ShiftRound(wide[at], q + 1 - 12));
                wide[at] = lpc[at] << (q + 1 - 12);
            }
        }
        else
        {
            for (var at = 0; at < order; at++)
            {
                lpc[at] = (short)ShiftRound(wide[at], q + 1 - 12);
            }
        }

        for (var step = 0; step < 16; step++)
        {
            if (InversePredictionGain(lpc) < (int)(1.0 / 1e4 * (1L << 30) + .5))
            {
                ExpandBandwidth(wide[..order], 65536 - (2 << step));
                for (var at = 0; at < order; at++)
                {
                    lpc[at] = (short)ShiftRound(wide[at], q + 1 - 12);
                }
            }
            else
            {
                break;
            }
        }
    }

    private static void FindPolynomial(Span<int> output, ReadOnlySpan<int> cosines, int start, int half)
    {
        const int q = 16;
        output[0] = 1 << q;
        output[1] = -cosines[start];
        for (var k = 1; k < half; k++)
        {
            var value = cosines[start + 2 * k];
            output[k + 1] = (output[k - 1] << 1) - (int)ShiftRound((long)value * output[k], q);
            for (var n = k; n > 1; n--)
            {
                output[n] += output[n - 2] - (int)ShiftRound((long)value * output[n - 1], q);
            }

            output[1] -= value;
        }
    }

    // silk_LPC_inverse_pred_gain, with RFC 8251, 6: a value past 32 bits is an unstable filter.
    private static int InversePredictionGain(ReadOnlySpan<short> lpc)
    {
        const int q = 24;
        const int limit = (int)(.99975 * (1 << q) + .5);
        var order = lpc.Length;
        Span<int> a = stackalloc int[2 * MostOrder];
        var current = a.Slice((order & 1) * MostOrder, MostOrder);
        var sum = 0;
        for (var at = 0; at < order; at++)
        {
            sum += lpc[at];
            current[at] = lpc[at] << (q - 12);
        }

        if (sum >= 4096)
        {
            return 0;
        }

        var inverseGain = 1 << 30;
        for (var k = order - 1; k > 0; k--)
        {
            if (current[k] > limit || current[k] < -limit)
            {
                return 0;
            }

            var reflection = -(current[k] << (31 - q));
            var multiplier1 = (1 << 30) - MulHigh(reflection, reflection);
            var shift = 32 - LeadingZeros(Math.Abs(multiplier1));
            var multiplier2 = Inverse(multiplier1, shift + 30);
            inverseGain = MulHigh(inverseGain, multiplier1) << 2;
            var old = current;
            current = a.Slice((k & 1) * MostOrder, MostOrder);
            for (var n = 0; n < k; n++)
            {
                var value = SubtractSaturate32(old[n], (int)ShiftRound((long)old[k - n - 1] * reflection, 31));
                var product = ShiftRound((long)value * multiplier2, shift);
                if (product > int.MaxValue || product < int.MinValue)
                {
                    return 0;
                }

                current[n] = (int)product;
            }
        }

        if (current[0] > limit || current[0] < -limit)
        {
            return 0;
        }

        var last = -(current[0] << (31 - q));
        var lastMultiplier = (1 << 30) - MulHigh(last, last);
        return MulHigh(inverseGain, lastMultiplier) << 2;
    }

    // silk_bwexpander.
    private static void ExpandBandwidth(Span<short> lpc, int chirp)
    {
        var minusOne = chirp - 65536;
        for (var at = 0; at < lpc.Length - 1; at++)
        {
            lpc[at] = (short)ShiftRound(chirp * lpc[at], 16);
            chirp += ShiftRound(chirp * minusOne, 16);
        }

        lpc[^1] = (short)ShiftRound(chirp * lpc[^1], 16);
    }

    // silk_bwexpander_32.
    private static void ExpandBandwidth(Span<int> lpc, int chirp)
    {
        var minusOne = chirp - 65536;
        for (var at = 0; at < lpc.Length - 1; at++)
        {
            lpc[at] = MulWW(chirp, lpc[at]);
            chirp += ShiftRound(chirp * minusOne, 16);
        }

        lpc[^1] = MulWW(chirp, lpc[^1]);
    }

    // silk_decode_pitch.
    private void DecodePitch()
    {
        var isLong = Subframes == 4;
        var codebook = Rate == 8
            ? isLong ? SilkTables.Lags2 : SilkTables.Lags2Ten
            : isLong ? SilkTables.Lags3 : SilkTables.Lags3Ten;
        var size = Rate == 8 ? isLong ? 11 : 3 : isLong ? 34 : 12;

        var lowest = 2 * Rate;
        var highest = 18 * Rate;
        var lag = lowest + _lagIndex;
        for (var subframe = 0; subframe < Subframes; subframe++)
        {
            _pitchLags[subframe] = Math.Clamp(lag + codebook[subframe * size + _contourIndex], lowest, highest);
        }
    }

    // silk_decode_core: the excitation from the pulses, then per subframe the LTP and LPC synthesis.
    private void Synthesize(Span<short> output)
    {
        var offsets = SilkTables.QuantizationOffsets;
        var offset = offsets[(_signalType >> 1) * 2 + _offsetType];
        var isInterpolated = _interpolation < 4;
        var seed = _seed;
        for (var at = 0; at < FrameLength; at++)
        {
            seed = Random(seed);
            var value = _pulses[at] << 14;
            if (value > 0)
            {
                value -= QuantizationAdjust << 4;
            }
            else if (value < 0)
            {
                value += QuantizationAdjust << 4;
            }

            value += offset << 4;
            if (seed < 0)
            {
                value = -value;
            }

            _excitation[at] = value;
            seed = unchecked(seed + _pulses[at]);
        }

        var lpcState = _lpc.AsSpan();
        _lpcState.CopyTo(lpcState);
        var ltpIndex = _historyLength;
        var order = _order;
        var subframeLength = SubframeLength;
        var lag = 0;
        for (var subframe = 0; subframe < Subframes; subframe++)
        {
            var a = _coefficients.AsSpan((subframe >> 1) * MostOrder, order);
            var b = _ltpFilters.AsSpan(subframe * LtpOrder, LtpOrder);
            var signalType = _signalType;
            var gainQ10 = _gains[subframe] >> 6;
            var inverseGain = Inverse(_gains[subframe], 47);
            int gainAdjust;
            if (_gains[subframe] != _previousGain)
            {
                gainAdjust = Divide(_previousGain, _gains[subframe], 16);
                for (var at = 0; at < MostOrder; at++)
                {
                    lpcState[at] = MulWW(gainAdjust, lpcState[at]);
                }
            }
            else
            {
                gainAdjust = 1 << 16;
            }

            _previousGain = _gains[subframe];
            if (LossCount != 0 && PreviousSignalType == Voiced && _signalType != Voiced && subframe < 2)
            {
                b.Clear();
                b[LtpOrder / 2] = (short)(.25 * (1 << 14) + .5);
                signalType = Voiced;
                _pitchLags[subframe] = LagPrevious;
            }

            var excitation = _excitation.AsSpan(subframe * subframeLength, subframeLength);
            ReadOnlySpan<int> residual = excitation;
            if (signalType == Voiced)
            {
                lag = _pitchLags[subframe];
                if (subframe == 0 || subframe == 2 && isInterpolated)
                {
                    var start = _historyLength - lag - order - LtpOrder / 2;
                    if (subframe == 2)
                    {
                        output[..(2 * subframeLength)].CopyTo(_history.AsSpan(_historyLength));
                    }

                    AnalysisFilter(_ltpSignal.AsSpan(start, _historyLength - start),
                        _history.AsSpan(start + subframe * subframeLength, _historyLength - start), a);
                    if (subframe == 0)
                    {
                        inverseGain = MulWB(inverseGain, _ltpScale) << 2;
                    }

                    for (var at = 0; at < lag + LtpOrder / 2; at++)
                    {
                        _ltpState[ltpIndex - at - 1] = MulWB(inverseGain, _ltpSignal[_historyLength - at - 1]);
                    }
                }
                else if (gainAdjust != 1 << 16)
                {
                    for (var at = 0; at < lag + LtpOrder / 2; at++)
                    {
                        _ltpState[ltpIndex - at - 1] = MulWW(gainAdjust, _ltpState[ltpIndex - at - 1]);
                    }
                }

                var lagged = ltpIndex - lag + LtpOrder / 2;
                var filtered = _residual.AsSpan(0, subframeLength);
                for (var at = 0; at < subframeLength; at++)
                {
                    var prediction = 2;
                    prediction = MulAddWB(prediction, _ltpState[lagged + at], b[0]);
                    prediction = MulAddWB(prediction, _ltpState[lagged + at - 1], b[1]);
                    prediction = MulAddWB(prediction, _ltpState[lagged + at - 2], b[2]);
                    prediction = MulAddWB(prediction, _ltpState[lagged + at - 3], b[3]);
                    prediction = MulAddWB(prediction, _ltpState[lagged + at - 4], b[4]);
                    filtered[at] = excitation[at] + (prediction << 1);
                    _ltpState[ltpIndex] = filtered[at] << 1;
                    ltpIndex++;
                }

                residual = filtered;
            }

            var samples = output.Slice(subframe * subframeLength, subframeLength);
            for (var at = 0; at < subframeLength; at++)
            {
                var prediction = order >> 1;
                for (var tap = 0; tap < order; tap++)
                {
                    prediction = MulAddWB(prediction, lpcState[MostOrder + at - tap - 1], a[tap]);
                }

                lpcState[MostOrder + at] = residual[at] + (prediction << 4);
                samples[at] = Saturate16(ShiftRound(MulWW(lpcState[MostOrder + at], gainQ10), 8));
            }

            lpcState.Slice(subframeLength, MostOrder).CopyTo(lpcState);
        }

        lpcState[..MostOrder].CopyTo(_lpcState);
    }

    // silk_LPC_analysis_filter: the residual of input by the LPC filter; its first order samples zero.
    private static void AnalysisFilter(Span<short> output, ReadOnlySpan<short> input, ReadOnlySpan<short> a)
    {
        var order = a.Length;
        for (var at = order; at < output.Length; at++)
        {
            var sum = 0;
            for (var tap = 0; tap < order; tap++)
            {
                sum = unchecked(sum + input[at - 1 - tap] * a[tap]);
            }

            sum = unchecked((input[at] << 12) - sum);
            output[at] = Saturate16(ShiftRound(sum, 12));
        }

        output[..order].Clear();
    }
}
