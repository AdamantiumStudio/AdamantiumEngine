namespace Adamantium.Pulsar.Decoding;

/// <summary>Opus (RFC 6716, 4) at 48 kHz: each frame of a packet by its mode - SILK for speech, CELT for music, both
/// for hybrid - with the redundant CELT frames and the cross-fades of 4.5 where the mode changes. One or two channels
/// out; a packet of the other number is heard in them.</summary>
internal sealed class OpusDecoder
{
    private const int Frame20 = 960;
    private const int Frame10 = 480;
    private const int Frame5 = 240;
    private const int Frame2 = 120;
    private const int LongestPacket = 5760;

    private readonly int _channels;
    private readonly CeltDecoder _celt;
    private readonly SilkDecoder _silk;
    private readonly int[] _starts = new int[OpusPacket.MostFrames];
    private readonly int[] _sizes = new int[OpusPacket.MostFrames];
    private readonly float[] _transition;
    private readonly float[] _redundant;
    private readonly float[] _silkOutput;
    private OpusMode _mode;
    private OpusMode _previousMode;
    private bool _wasRedundant;
    private int _frameSize = Frame2;
    private int _endBand;
    private int _silkRate;
    private int _streamChannels;

    public OpusDecoder(int channels)
    {
        _channels = channels;
        _celt = new CeltDecoder(channels);
        _silk = new SilkDecoder(channels);
        _transition = new float[Frame5 * channels];
        _redundant = new float[Frame5 * channels];
        _silkOutput = new float[LongestPacket * channels];
        _streamChannels = channels;
    }

    /// <summary>The range decoder's state at the end of the last packet, xored with its redundant frame's: as the
    /// encoder's, where the packet was read as written.</summary>
    public uint FinalRange { get; private set; }

    /// <summary>Of a packet, its samples a channel, or -1 for one that is no Opus packet.</summary>
    public static int SamplesOf(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return -1;
        }

        var count = (packet[0] & 3) switch { 0 => 1, 1 or 2 => 2, _ => packet.Length < 2 ? -1 : packet[1] & 0x3F };
        return count < 0 ? -1 : count * OpusPacket.FrameSizeOf(packet[0]);
    }

    public void Reset()
    {
        _celt.Reset();
        _silk.Reset();
        _previousMode = OpusMode.None;
        _wasRedundant = false;
        _frameSize = Frame2;
    }

    /// <summary>A packet's samples into <paramref name="output"/>, the channels side by side; how many a channel, or
    /// -1 for a packet that is damaged. An empty packet is one lost: <paramref name="lostSamples"/> are concealed.
    /// </summary>
    public int Decode(ReadOnlySpan<byte> packet, Span<float> output, bool isSelfDelimited = false, int lostSamples = 0)
    {
        if (packet.IsEmpty)
        {
            var done = 0;
            while (done < lostSamples)
            {
                var size = Math.Min(lostSamples - done, Frame20);
                done += DecodeFrame(default, output[(done * _channels)..], size);
            }

            return done;
        }

        var toc = packet[0];
        _mode = OpusPacket.ModeOf(toc);
        _endBand = OpusPacket.EndBandOf(toc);
        _silkRate = OpusPacket.SilkRateOf(toc);
        _frameSize = OpusPacket.FrameSizeOf(toc);
        _streamChannels = OpusPacket.ChannelsOf(toc);
        var count = OpusPacket.Parse(packet, isSelfDelimited, _starts, _sizes, out _);
        if (count < 0 || count * _frameSize * _channels > output.Length)
        {
            return -1;
        }

        var samples = 0;
        for (var frame = 0; frame < count; frame++)
        {
            var decoded = DecodeFrame(packet.Slice(_starts[frame], _sizes[frame]), output[(samples * _channels)..], _frameSize);
            if (decoded < 0)
            {
                return -1;
            }

            samples += decoded;
        }

        return samples;
    }

    /// <summary>The bytes a self-delimited stream of a multistream packet takes, or -1.</summary>
    public static int LengthOf(ReadOnlySpan<byte> packet, bool isSelfDelimited)
    {
        Span<int> starts = stackalloc int[OpusPacket.MostFrames];
        Span<int> sizes = stackalloc int[OpusPacket.MostFrames];
        return OpusPacket.Parse(packet, isSelfDelimited, starts, sizes, out var length) < 0 ? -1 : length;
    }

    // opus_decode_frame: one frame, or one lost where data is empty.
    private int DecodeFrame(ReadOnlySpan<byte> data, Span<float> output, int frameSize)
    {
        var isLost = data.Length <= 1;
        int size;
        OpusMode mode;
        if (!isLost)
        {
            size = _frameSize;
            mode = _mode;
        }
        else
        {
            data = default;
            size = Math.Min(frameSize, _frameSize);
            if (_previousMode == OpusMode.None)
            {
                output[..(size * _channels)].Clear();
                return size;
            }

            mode = _previousMode;
            // Lost CELT longer than 20 ms is concealed 20 ms at a time.
            if (size > Frame20 && mode != OpusMode.Silk)
            {
                var done = 0;
                while (done < size)
                {
                    done += DecodeFrame(default, output[(done * _channels)..], Math.Min(Frame20, size - done));
                }

                return size;
            }
        }

        var decoder = new OpusRangeDecoder(data);
        var isTransition = false;
        if (!isLost && _previousMode != OpusMode.None &&
            (mode == OpusMode.Celt && _previousMode != OpusMode.Celt && !_wasRedundant ||
            mode != OpusMode.Celt && _previousMode == OpusMode.Celt))
        {
            isTransition = true;
            if (mode == OpusMode.Celt)
            {
                ConcealInto(_transition, Math.Min(Frame5, size));
            }
        }

        var channels = _channels;
        if (mode != OpusMode.Celt)
        {
            if (_previousMode == OpusMode.Celt)
            {
                _silk.Reset();
            }

            var silk = _silkOutput.AsSpan(0, Math.Max(Frame10, size) * channels);
            var isFirst = true;
            var done = 0;
            var milliseconds = Math.Max(10, size / 48);
            while (done < size)
            {
                var decoded = _silk.Decode(ref decoder, isLost, isFirst, _silkRate, _streamChannels, milliseconds,
                    silk[(done * channels)..]);
                if (decoded < 0)
                {
                    if (!isLost)
                    {
                        return -1;
                    }

                    silk[(done * channels)..(size * channels)].Clear();
                    decoded = size - done;
                }

                done += decoded;
                isFirst = false;
            }
        }

        var startBand = 0;
        var isRedundant = false;
        var isCeltToSilk = false;
        var redundantBytes = 0;
        var length = data.Length;
        if (!isLost && mode != OpusMode.Celt && decoder.Tell() + 17 + (mode == OpusMode.Hybrid ? 20 : 0) <= 8 * length)
        {
            isRedundant = mode != OpusMode.Hybrid || decoder.DecodeBit(12);
            if (isRedundant)
            {
                isCeltToSilk = decoder.DecodeBit(1);
                redundantBytes = mode == OpusMode.Hybrid
                    ? (int)decoder.DecodeUniform(256) + 2
                    : length - ((decoder.Tell() + 7) >> 3);
                length -= redundantBytes;
                if (length * 8 < decoder.Tell())
                {
                    length = 0;
                    redundantBytes = 0;
                    isRedundant = false;
                }

                decoder.Shrink(redundantBytes);
            }
        }

        if (mode != OpusMode.Celt)
        {
            startBand = 17;
        }

        _celt.EndBand = _endBand;
        _celt.StreamChannels = _streamChannels;
        if (isRedundant)
        {
            isTransition = false;
        }

        if (isTransition && mode != OpusMode.Celt)
        {
            ConcealInto(_transition, Math.Min(Frame5, size));
        }

        uint redundantRange = 0;
        if (isRedundant && isCeltToSilk)
        {
            _celt.StartBand = 0;
            redundantRange = DecodeRedundant(data.Slice(length, redundantBytes));
        }

        _celt.StartBand = startBand;
        var samples = output[..(size * channels)];
        if (mode != OpusMode.Silk)
        {
            var celtSize = Math.Min(Frame20, size);
            if (mode != _previousMode && _previousMode != OpusMode.None && !_wasRedundant)
            {
                _celt.Reset();
            }

            if (isLost)
            {
                _celt.Conceal(samples, celtSize, false);
            }
            else
            {
                _celt.Decode(ref decoder, samples, celtSize, false);
            }
        }
        else
        {
            samples.Clear();
            if (_previousMode == OpusMode.Hybrid && !(isRedundant && isCeltToSilk && _wasRedundant))
            {
                // What the CELT layer had left, faded out by a silent frame.
                _celt.StartBand = 0;
                var silent = new OpusRangeDecoder([0xFF, 0xFF]);
                _celt.StreamChannels = _streamChannels;
                _celt.Decode(ref silent, samples, Frame2, false);
            }
        }

        if (mode != OpusMode.Celt)
        {
            var silk = _silkOutput.AsSpan(0, size * channels);
            for (var at = 0; at < samples.Length; at++)
            {
                samples[at] += silk[at];
            }
        }

        var window = CeltTables.Window;
        if (isRedundant && !isCeltToSilk)
        {
            _celt.Reset();
            _celt.StartBand = 0;
            redundantRange = DecodeRedundant(data.Slice(length, redundantBytes));
            var tail = samples[((size - Frame2) * channels)..];
            Fade(tail, _redundant.AsSpan(Frame2 * channels), tail, window);
        }

        if (isRedundant && isCeltToSilk)
        {
            _redundant.AsSpan(0, Frame2 * channels).CopyTo(samples);
            var rest = samples[(Frame2 * channels)..];
            Fade(_redundant.AsSpan(Frame2 * channels), rest, rest, window);
        }

        if (isTransition)
        {
            if (size >= Frame5)
            {
                _transition.AsSpan(0, Frame2 * channels).CopyTo(samples);
                var rest = samples[(Frame2 * channels)..];
                Fade(_transition.AsSpan(Frame2 * channels), rest, rest, window);
            }
            else
            {
                Fade(_transition, samples, samples, window);
            }
        }

        FinalRange = data.Length <= 1 ? 0 : decoder.FinalRange ^ redundantRange;
        _previousMode = mode;
        _wasRedundant = isRedundant && !isCeltToSilk;
        return size;
    }

    // A redundant CELT frame of 5 ms, into _redundant; its final range.
    private uint DecodeRedundant(ReadOnlySpan<byte> bytes)
    {
        var redundant = new OpusRangeDecoder(bytes);
        _celt.Decode(ref redundant, _redundant, Frame5, false);
        return redundant.FinalRange;
    }

    // What the mode before would have given next, as for a frame lost, for a transition's fade.
    private void ConcealInto(float[] target, int size) => DecodeFrame(default, target, size);

    // smooth_fade: from first to second over 2.5 ms, by the square of the window.
    private void Fade(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> output, float[] window)
    {
        for (var channel = 0; channel < _channels; channel++)
        {
            for (var at = 0; at < Frame2; at++)
            {
                var weight = window[at] * window[at];
                var index = at * _channels + channel;
                output[index] = weight * second[index] + (1 - weight) * first[index];
            }
        }
    }
}
