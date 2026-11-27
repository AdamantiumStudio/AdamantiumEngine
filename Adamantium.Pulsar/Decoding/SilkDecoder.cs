using static Adamantium.Pulsar.Decoding.SilkMath;

namespace Adamantium.Pulsar.Decoding;

/// <summary>The SILK layer of Opus (RFC 6716, 4.2): of an Opus frame, its header flags and LBRR frames read, then one
/// 10 or 20 ms SILK frame a call - mid and, where coded, side - unmixed to left and right, and resampled to 48 kHz.
/// LBRR frames are read past, not played; a frame lost is silence.</summary>
internal sealed class SilkDecoder
{
    private const int MostFrame = 320;
    private const int InterpolationMilliseconds = 8;

    private readonly int _apiChannels;
    private readonly SilkChannel[] _channels = [new(), new()];
    private readonly short[][] _decoded = [new short[MostFrame + 2], new short[MostFrame + 2]];
    private readonly short[] _resampled = new short[960];
    private readonly int[] _previousWeights = new int[2];
    private readonly short[] _mid = new short[2];
    private readonly short[] _side = new short[2];
    private int _internalChannels;
    private int _previousApiChannels;
    private bool _wasMidOnly;

    public SilkDecoder(int channels) => _apiChannels = channels;

    /// <summary>silk_InitDecoder, with RFC 8251, 3: the stereo state too.</summary>
    public void Reset()
    {
        foreach (var channel in _channels)
        {
            channel.Reset();
        }

        Array.Clear(_previousWeights);
        Array.Clear(_mid);
        Array.Clear(_side);
        _wasMidOnly = false;
    }

    /// <summary>silk_Decode: the next SILK frame of the Opus frame into <paramref name="output"/> at 48 kHz, its
    /// channels side by side; how many samples a channel.</summary>
    /// <param name="decoder">The Opus frame's range decoder.</param>
    /// <param name="isLost">Whether the frame was lost and is concealed instead.</param>
    /// <param name="isFirst">The first of the Opus frame, whose header bits are read before it.</param>
    /// <param name="rate">The SILK frame's internal sample rate.</param>
    /// <param name="streamChannels">How many channels the stream has.</param>
    /// <param name="milliseconds">The Opus frame's length: 10, 20, 40 or 60.</param>
    /// <param name="output">Where the samples go.</param>
    public int Decode(ref OpusRangeDecoder decoder, bool isLost, bool isFirst, int rate, int streamChannels,
        int milliseconds, Span<float> output)
    {
        var channels = _channels;
        if (isFirst)
        {
            for (var at = 0; at < streamChannels; at++)
            {
                channels[at].FramesDecoded = 0;
            }
        }

        if (streamChannels > _internalChannels)
        {
            channels[1].Reset();
        }

        var kHz = (rate >> 10) + 1;
        var isStereoToMono = streamChannels == 1 && _internalChannels == 2 && kHz == channels[0].Rate;
        if (channels[0].FramesDecoded == 0)
        {
            for (var at = 0; at < streamChannels; at++)
            {
                var (frames, subframes) = milliseconds switch
                {
                    10 => (1, 2),
                    20 => (1, 4),
                    40 => (2, 4),
                    60 => (3, 4),
                    _ => (0, 0),
                };
                if (frames == 0)
                {
                    return -1;
                }

                channels[at].FramesPerPacket = frames;
                channels[at].SetRate(kHz, subframes);
            }
        }

        if (_apiChannels == 2 && streamChannels == 2 && (_previousApiChannels == 1 || _internalChannels == 1))
        {
            Array.Clear(_previousWeights);
            Array.Clear(_side);
            channels[1].Resampler.CopyFrom(channels[0].Resampler);
        }

        _previousApiChannels = _apiChannels;
        _internalChannels = streamChannels;
        Span<int> weights = stackalloc int[2];
        var isMidOnly = false;
        if (!isLost && channels[0].FramesDecoded == 0)
        {
            ReadHeader(ref decoder, streamChannels, ref isMidOnly, weights);
        }

        var frame = channels[0].FramesDecoded;
        if (streamChannels == 2)
        {
            if (!isLost)
            {
                DecodeWeights(ref decoder, weights);
                isMidOnly = !channels[1].ActiveFlags[frame] && decoder.DecodeInverse(SilkTables.MidOnlyInverse, 8) != 0;
            }
            else
            {
                weights[0] = _previousWeights[0];
                weights[1] = _previousWeights[1];
            }
        }

        if (streamChannels == 2 && !isMidOnly && _wasMidOnly)
        {
            channels[1].ResetSide();
        }

        var hasSide = !isLost ? !isMidOnly : !_wasMidOnly;
        var length = channels[0].FrameLength;
        for (var at = 0; at < streamChannels; at++)
        {
            var samples = _decoded[at].AsSpan(2, length);
            if (at == 0 || hasSide)
            {
                // Of the first channel, already counted for the second.
                var index = channels[0].FramesDecoded - at;
                var coding = index <= 0
                    ? SilkChannel.Independent
                    : at > 0 && _wasMidOnly ? SilkChannel.IndependentNoLtpScaling : SilkChannel.Conditional;
                channels[at].Decode(ref decoder, samples, isLost, coding);
            }
            else
            {
                samples.Clear();
            }

            channels[at].FramesDecoded++;
        }

        if (_apiChannels == 2 && streamChannels == 2)
        {
            Unmix(weights, channels[0].Rate, length);
        }
        else
        {
            _mid.CopyTo(_decoded[0], 0);
            _decoded[0].AsSpan(length, 2).CopyTo(_mid);
        }

        var count = length * 48 / channels[0].Rate;
        var resampled = _resampled.AsSpan(0, count);
        for (var at = 0; at < Math.Min(_apiChannels, streamChannels); at++)
        {
            channels[at].Resampler.Resample(resampled, _decoded[at].AsSpan(1, length));
            Write(resampled, output, at);
        }

        if (_apiChannels == 2 && streamChannels == 1)
        {
            if (isStereoToMono)
            {
                channels[1].Resampler.Resample(resampled, _decoded[0].AsSpan(1, length));
            }

            Write(resampled, output, 1);
        }

        if (isLost)
        {
            for (var at = 0; at < _internalChannels; at++)
            {
                channels[at].ForgetGain();
            }
        }
        else
        {
            _wasMidOnly = isMidOnly;
        }

        return count;
    }

    private void Write(ReadOnlySpan<short> samples, Span<float> output, int channel)
    {
        for (var at = 0; at < samples.Length; at++)
        {
            output[at * _apiChannels + channel] = samples[at] * (1f / 32768);
        }
    }

    // The VAD and LBRR flags of each channel, then the LBRR frames, read past.
    private void ReadHeader(ref OpusRangeDecoder decoder, int streamChannels, ref bool isMidOnly, scoped Span<int> weights)
    {
        var channels = _channels;
        for (var at = 0; at < streamChannels; at++)
        {
            for (var frame = 0; frame < channels[at].FramesPerPacket; frame++)
            {
                channels[at].ActiveFlags[frame] = decoder.DecodeBit(1);
            }

            channels[at].HasRedundancy = decoder.DecodeBit(1);
        }

        for (var at = 0; at < streamChannels; at++)
        {
            var flags = channels[at].RedundantFlags;
            Array.Clear(flags);
            if (!channels[at].HasRedundancy)
            {
                continue;
            }

            if (channels[at].FramesPerPacket == 1)
            {
                flags[0] = true;
            }
            else
            {
                var table = channels[at].FramesPerPacket == 2 ? SilkTables.Redundancy2Inverse : SilkTables.Redundancy3Inverse;
                var symbol = decoder.DecodeInverse(table, 8) + 1;
                for (var frame = 0; frame < channels[at].FramesPerPacket; frame++)
                {
                    flags[frame] = (symbol >> frame & 1) != 0;
                }
            }
        }

        for (var frame = 0; frame < channels[0].FramesPerPacket; frame++)
        {
            for (var at = 0; at < streamChannels; at++)
            {
                if (!channels[at].RedundantFlags[frame])
                {
                    continue;
                }

                if (streamChannels == 2 && at == 0)
                {
                    DecodeWeights(ref decoder, weights);
                    if (!channels[1].RedundantFlags[frame])
                    {
                        isMidOnly = decoder.DecodeInverse(SilkTables.MidOnlyInverse, 8) != 0;
                    }
                }

                var coding = frame > 0 && channels[at].RedundantFlags[frame - 1] ? SilkChannel.Conditional : SilkChannel.Independent;
                channels[at].Skip(ref decoder, frame, coding);
            }
        }
    }

    // silk_stereo_decode_pred.
    private static void DecodeWeights(ref OpusRangeDecoder decoder, scoped Span<int> weights)
    {
        Span<int> indices = stackalloc int[6];
        var joint = decoder.DecodeInverse(SilkTables.StereoJointInverse, 8);
        indices[2] = joint / 5;
        indices[5] = joint - 5 * indices[2];
        for (var at = 0; at < 2; at++)
        {
            indices[3 * at] = decoder.DecodeInverse(SilkTables.Uniform3, 8);
            indices[3 * at + 1] = decoder.DecodeInverse(SilkTables.Uniform5, 8);
        }

        var table = SilkTables.StereoWeights;
        for (var at = 0; at < 2; at++)
        {
            var index = indices[3 * at] + 3 * indices[3 * at + 2];
            var low = table[index];
            var step = MulWB(table[index + 1] - low, (int)(.5 / 5 * 65536 + .5));
            weights[at] = MulAddBB(low, step, 2 * indices[3 * at + 1] + 1);
        }

        weights[0] -= weights[1];
    }

    // silk_stereo_MS_to_LR: the side predicted from the mid, weights interpolated over 8 ms, then left and right.
    private void Unmix(ReadOnlySpan<int> weights, int kHz, int length)
    {
        var mid = _decoded[0];
        var side = _decoded[1];
        _mid.CopyTo(mid, 0);
        _side.CopyTo(side, 0);
        mid.AsSpan(length, 2).CopyTo(_mid);
        side.AsSpan(length, 2).CopyTo(_side);
        var weight0 = _previousWeights[0];
        var weight1 = _previousWeights[1];
        var steps = InterpolationMilliseconds * kHz;
        var denominator = (1 << 16) / steps;
        var delta0 = ShiftRound(MulBB(weights[0] - _previousWeights[0], denominator), 16);
        var delta1 = ShiftRound(MulBB(weights[1] - _previousWeights[1], denominator), 16);
        for (var at = 0; at < length; at++)
        {
            if (at < steps)
            {
                weight0 += delta0;
                weight1 += delta1;
            }
            else if (at == steps)
            {
                weight0 = weights[0];
                weight1 = weights[1];
            }

            var sum = (mid[at] + mid[at + 2] + (mid[at + 1] << 1)) << 9;
            sum = MulAddWB(side[at + 1] << 8, sum, weight0);
            sum = MulAddWB(sum, mid[at + 1] << 11, weight1);
            side[at + 1] = Saturate16(ShiftRound(sum, 8));
        }

        _previousWeights[0] = weights[0];
        _previousWeights[1] = weights[1];
        for (var at = 0; at < length; at++)
        {
            var left = mid[at + 1] + side[at + 1];
            var right = mid[at + 1] - side[at + 1];
            mid[at + 1] = Saturate16(left);
            side[at + 1] = Saturate16(right);
        }
    }
}
