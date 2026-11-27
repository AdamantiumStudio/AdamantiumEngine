namespace Adamantium.Pulsar.Decoding;

/// <summary>MP3 frames, one after another, into samples: the bit reservoir the frames share and the filters they run
/// through are kept from one to the next. Of a file read as it is laid out, or of a stream read as it comes.</summary>
internal sealed class Mp3FrameDecoder
{
    // A frame takes this much of the frames before: main_data_begin's most.
    public const int LongestReservoir = 511;
    private const int ReservoirCapacity = 4096;

    private readonly Mp3Layer3 _layer3 = new();
    private readonly byte[] _reservoir = new byte[ReservoirCapacity];
    private int _reservoirLength;

    /// <summary>What is kept of the frames before is let go: the next is decoded as if it were the first.</summary>
    public void Reset()
    {
        _layer3.Reset();
        _reservoirLength = 0;
    }

    /// <summary>The frame - its header included - into <paramref name="decoded"/>, the header's samples of
    /// <paramref name="channels"/> each: a frame of one channel in a sound of two is heard in both. A frame whose main
    /// data begins before what is kept is silence.</summary>
    public void Decode(Mp3FrameHeader header, ReadOnlySpan<byte> frame, Span<float> decoded, int channels)
    {
        var samples = decoded[..(header.Samples * header.Channels)];
        var sideEnd = header.SideInfoAt + header.SideInfoLength;
        if (sideEnd > frame.Length ||
            !_layer3.ReadSideInfo(header, frame.Slice(header.SideInfoAt, header.SideInfoLength), out var begin))
        {
            decoded[..(header.Samples * channels)].Clear();
            _reservoirLength = 0;
            return;
        }

        // The main data begins that far back in the frames before; what is kept of them is no more than it takes.
        var mainData = frame[sideEnd..];
        var keep = Math.Min(_reservoirLength, LongestReservoir);
        _reservoir.AsSpan(_reservoirLength - keep, keep).CopyTo(_reservoir);
        _reservoirLength = keep;
        var hasMainData = begin <= _reservoirLength;
        var mainStart = _reservoirLength - begin;
        mainData.CopyTo(_reservoir.AsSpan(_reservoirLength));
        _reservoirLength += mainData.Length;
        if (hasMainData)
        {
            _layer3.Decode(header, _reservoir.AsSpan(mainStart, _reservoirLength - mainStart), samples);
        }
        else
        {
            samples.Clear();
        }

        // A frame of one channel in a sound of two: from the last sample back, each in both.
        if (header.Channels < channels)
        {
            _layer3.ShareFirstChannel();
            for (var sample = header.Samples - 1; sample >= 0; sample--)
            {
                decoded[sample * 2 + 1] = decoded[sample * 2] = decoded[sample];
            }
        }
    }
}
