using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>Opus in Ogg (RFC 7845) at 48 kHz: its identification and comment headers, then each packet's streams -
/// one, or as mapping family 1 or 255 has them, several of one or two channels - mapped to the channels out, with the
/// header's output gain. The first pre-skip samples are not the sound's; a page's granule is where its last packet
/// ends, the last page's where the sound does. A seek decodes 80 ms before the sample sought, as 4.6 asks.</summary>
internal sealed class OggOpusDecoder : ISoundDecoder
{
    private const int PreRoll = 3840;
    private const int LongestPacket = 5760;

    private readonly OggStream _ogg;
    private readonly int _channels;
    private readonly int _coupled;
    private readonly byte[] _mapping;
    private readonly OpusDecoder[] _streams;
    private readonly float[][] _decoded;
    private readonly int[] _outputOrder;
    private readonly float _gain;
    private readonly int _audioPage;
    // Of the stream, in granules: where its first packet begins, and where its sound does, after pre-skip.
    private readonly long _first;
    private readonly long _start;
    private readonly DecodedSamples _ready;
    private long _position;

    /// <param name="ogg">At its first packet; disposed with it, or where it is refused.</param>
    public OggOpusDecoder(OggStream ogg)
    {
        _ogg = ogg;
        try
        {
            // Whether it is of what is read, as the stream was chosen by; then what only damage makes wrong.
            if (!ogg.TryReadPacket(out var head, out _, out _) || !IsReadable(head))
            {
                throw new InvalidDataException(
                    "The Opus stream's identification header is of a version, a channel mapping or a number of channels not read.");
            }

            _channels = head[9];
            var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(head[10..]);
            var gain = BinaryPrimitives.ReadInt16LittleEndian(head[16..]);
            var family = head[18];
            int streams;
            if (family == 0)
            {
                streams = 1;
                _coupled = _channels - 1;
                _mapping = _channels == 1 ? [0] : [0, 1];
            }
            else
            {
                streams = head[19];
                _coupled = head[20];
                _mapping = head.Slice(21, _channels).ToArray();
                if (streams == 0 || _coupled > streams || streams + _coupled > 255 ||
                    _mapping.Any(index => index != 255 && index >= streams + _coupled))
                {
                    throw Damaged("its channel mapping names streams there are not");
                }
            }

            _outputOrder = family == 1 ? VorbisChannels.SpeakerIndices(_channels) : [.. Enumerable.Range(0, _channels)];
            _streams = [.. Enumerable.Range(0, streams).Select(stream => new OpusDecoder(stream < _coupled ? 2 : 1))];
            _decoded = [.. Enumerable.Range(0, streams).Select(stream => new float[LongestPacket * (stream < _coupled ? 2 : 1)])];
            _gain = (float)Math.Pow(10, gain / (20.0 * 256));
            Format = new AudioFormat(48000, _channels);
            if (!ogg.TryReadPacket(out var tags, out _, out _) || tags.Length < 8 || !tags.StartsWith("OpusTags"u8))
            {
                throw Damaged("its comment header is not after the identification");
            }

            var comments = VorbisComments.Read(tags[8..]);
            _audioPage = ogg.NextPage;
            _first = FirstGranule();
            _start = _first + preSkip;
            Frames = Math.Max(0, LastGranule() - _start);
            Loop = comments.LoopWithin(Frames);
            _ready = new DecodedSamples(_channels, LongestPacket);
            Restart(_audioPage, _first);
            _position = _start;
        }
        catch
        {
            _ogg.Dispose();
            throw;
        }
    }

    public AudioFormat Format { get; }

    public long Frames { get; }

    public SoundLoop? Loop { get; }

    /// <summary>Whether <paramref name="packet"/>, a first one, is Opus's identification header.</summary>
    public static bool IsOpus(ReadOnlySpan<byte> packet) => packet.StartsWith("OpusHead"u8);

    /// <summary>Whether <paramref name="packet"/> is the identification header of a stream this reads: of a version
    /// before 16, mapping family 0 of one or two channels, or 1 or 255 of up to eight.</summary>
    public static bool IsReadable(ReadOnlySpan<byte> packet) =>
        IsOpus(packet) && packet.Length >= 19 && packet[8] < 16 && packet[9] > 0 && (packet[18] switch
        {
            0 => packet[9] <= 2,
            1 or 255 => packet[9] <= AudioFormat.MostChannels && packet.Length >= 21 + packet[9],
            _ => false,
        });

    public int Read(Span<float> samples)
    {
        var wanted = (int)Math.Min(samples.Length / _channels, _start + Frames - _position);
        var done = 0;
        while (done < wanted)
        {
            if (_ready.Count == 0 || !_ready.IsPlaced)
            {
                if (!DecodeNext())
                {
                    break;
                }

                continue;
            }

            done += _ready.Take(samples[(done * _channels)..], ref _position, wanted - done);
        }

        return done;
    }

    // From a page that begins a packet, its granule before the sample sought by 80 ms at least.
    public void Seek(long frame)
    {
        if (frame < 0 || frame > Frames)
        {
            throw new InvalidDataException($"The Opus stream has no frame {frame}.");
        }

        var sample = _start + frame;
        var pages = _ogg.Pages;
        var page = _audioPage;
        var start = _first;
        // Checked once it is found by the granules: only that page is read.
        for (var at = pages.Count - 1; at > _audioPage; at--)
        {
            if (!pages[at].IsContinued && GranuleBefore(at, false) is var before and >= 0 && before <= sample - PreRoll &&
                GranuleBefore(at) >= 0)
            {
                page = at;
                start = before;
                break;
            }
        }

        Restart(page, start);
        _position = sample;
    }

    public void Dispose() => _ogg.Dispose();

    // Decoding from the first packet that begins on the page, with the decoders as at the beginning: placed at
    // <paramref name="start"/>, where the page before ended, the stream's start for the first.
    private void Restart(int page, long start)
    {
        _ogg.SeekToPage(page);
        foreach (var stream in _streams)
        {
            stream.Reset();
        }

        _ready.Clear();
        _ready.Place(start);
    }

    // The next packet's samples after those ready; false past the last.
    private bool DecodeNext()
    {
        if (!_ogg.TryReadPacket(out var packet, out var granule, out var afterLoss))
        {
            return false;
        }

        if (afterLoss)
        {
            // Placed again by the next granule.
            _ready.Clear();
        }

        var count = OpusDecoder.SamplesOf(packet);
        if (count <= 0 || count > LongestPacket)
        {
            return true;
        }

        var target = _ready.Reserve(count);
        if (!DecodeStreams(packet, count, target))
        {
            target.Clear();
        }

        _ready.Add(count);
        if (!_ready.IsPlaced && granule >= 0)
        {
            // The last page's granule may cut the end; where it begins a packet, its packets begin where the page
            // before ended - by that one's granule, a damaged page's too where it is between where they would begin
            // cut and not. A continued one's, after a loss, are placed as if none were cut.
            var page = _ogg.NextPage - 1;
            var info = _ogg.Pages[page];
            var before = info.IsLast && !info.IsContinued ? GranuleBefore(page, false) : -1;
            _ready.Place(before > granule - _ready.Count && before <= granule ? before : granule - _ready.Count);
        }

        return true;
    }

    // Each stream of the packet, then the channels out from them; false where one is damaged.
    private bool DecodeStreams(ReadOnlySpan<byte> packet, int count, Span<float> target)
    {
        var at = 0;
        for (var stream = 0; stream < _streams.Length; stream++)
        {
            var isLast = stream == _streams.Length - 1;
            var length = isLast ? packet.Length - at : OpusDecoder.LengthOf(packet[at..], true);
            if (length <= 0 || at + length > packet.Length ||
                _streams[stream].Decode(packet.Slice(at, length), _decoded[stream], !isLast) != count)
            {
                return false;
            }

            at += length;
        }

        for (var channel = 0; channel < _channels; channel++)
        {
            var index = _mapping[channel];
            var output = _outputOrder[channel];
            if (index == 255)
            {
                for (var sample = 0; sample < count; sample++)
                {
                    target[sample * _channels + output] = 0;
                }

                continue;
            }

            var isCoupled = index < 2 * _coupled;
            var decoded = _decoded[isCoupled ? index / 2 : index - _coupled];
            var width = isCoupled ? 2 : 1;
            var offset = isCoupled ? index & 1 : 0;
            for (var sample = 0; sample < count; sample++)
            {
                target[sample * _channels + output] = decoded[sample * width + offset] * _gain;
            }
        }

        return true;
    }

    // 4.5: the first page's granule less its packets' samples; 0 where that page is the last and has fewer, or is
    // damaged, as an encoder's stream begins.
    private long FirstGranule()
    {
        _ogg.SeekToPage(_audioPage);
        long samples = 0;
        while (_ogg.TryReadPacket(out var packet, out var granule, out var afterLoss))
        {
            if (afterLoss)
            {
                return 0;
            }

            samples += Math.Max(0, OpusDecoder.SamplesOf(packet));
            if (granule >= 0)
            {
                var start = granule - samples;
                if (start < 0 && !_ogg.Pages[_ogg.NextPage - 1].IsLast)
                {
                    throw Damaged("its first page says fewer samples than its packets have");
                }

                return Math.Max(0, start);
            }
        }

        throw Damaged("it has no audio its pages place");
    }

    // Of the nearest page before with a granule: the granule, as its header says; -1 where a page between them is not
    // there, by their numbers, or where that page is not whole, when asked to check, as a damaged one's could be
    // anything. Before the first audio page, the stream's start.
    private long GranuleBefore(int page, bool isChecked = true)
    {
        var pages = _ogg.Pages;
        for (var at = page - 1; at >= _audioPage; at--)
        {
            if (pages[at].Sequence + 1 != pages[at + 1].Sequence)
            {
                return -1;
            }

            if (pages[at].Granule >= 0)
            {
                return !isChecked || _ogg.IsWhole(at) ? pages[at].Granule : -1;
            }
        }

        return _first;
    }

    // Of the last page whole: a damaged one's could say any length.
    private long LastGranule()
    {
        var pages = _ogg.Pages;
        for (var at = pages.Count - 1; at >= _audioPage; at--)
        {
            if (pages[at].Granule >= 0 && _ogg.IsWhole(at))
            {
                return pages[at].Granule;
            }
        }

        return _first;
    }

    private static InvalidDataException Damaged(string how) => new($"The Opus stream is damaged: {how}.");
}
