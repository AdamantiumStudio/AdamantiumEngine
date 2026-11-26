using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>Vorbis I in Ogg, from the specification: its three headers, then audio packets, each a block of floors and
/// residues coupled, transformed, windowed and overlapped with the one before. Where a sample is in the sound is known by
/// the granule of the page a packet ends, so it reads from the stream's start, after a seek and after a page lost alike:
/// what it decodes waits until a granule says where it is. Samples before the first granule's beginning are not the
/// sound's, nor those past the last one's end.</summary>
internal sealed class VorbisDecoder : ISoundDecoder
{
    // A seek begins decoding this far back, so that its first sample is before the one sought.
    private const int SeekMargin = 2;

    private readonly OggStream _ogg;
    private readonly int _channels;
    private readonly int _shortBlock;
    private readonly int _longBlock;
    private readonly VorbisCodebook[] _books;
    private readonly VorbisFloor[] _floors;
    private readonly VorbisResidue[] _residues;
    private readonly VorbisMapping[] _mappings;
    private readonly bool[] _modeLong;
    private readonly int[] _modeMappings;
    private readonly int _modeBits;
    private readonly VorbisTransform _shortTransform;
    private readonly VorbisTransform _longTransform;
    private readonly float[] _shortSlope;
    private readonly float[] _longSlope;
    private readonly int[] _speakerOrder;
    private readonly int _audioPage;
    private readonly long _start;
    private readonly float[][] _spectra;
    private float[][] _previous;
    private float[][] _current;
    private readonly bool[] _floorUsed;
    private readonly bool[] _noResidue;
    private int _previousBlock;
    // Decoded from the first audio packet on, none lost.
    private bool _isFromStart;
    private readonly DecodedSamples _ready;
    // Of the stream, the next sample read.
    private long _position;

    /// <param name="ogg">At its first packet; disposed with it, or where it is refused.</param>
    public VorbisDecoder(OggStream ogg)
    {
        _ogg = ogg;
        try
        {
            // 4.2.2, after the packet's type and "vorbis": version, channels, rate, three bit rates, block sizes.
            var identification = NextHeader(1);
            if (identification.Length < 23)
            {
                throw Damaged("its identification is cut short");
            }

            _channels = identification[4];
            var rate = BinaryPrimitives.ReadInt32LittleEndian(identification[5..]);
            _shortBlock = 1 << (identification[21] & 15);
            _longBlock = 1 << (identification[21] >> 4);
            if (_channels > AudioFormat.MostChannels)
            {
                throw new InvalidDataException(
                    $"The Vorbis stream has {_channels} channels; no more than {AudioFormat.MostChannels} are read.");
            }

            if (BinaryPrimitives.ReadInt32LittleEndian(identification) != 0 || _channels == 0 || rate <= 0 ||
                rate > AudioFormat.HighestRate || _shortBlock < 64 || _longBlock > 8192 || _shortBlock > _longBlock ||
                (identification[22] & 1) == 0)
            {
                throw Damaged("its identification is of no stream this decoder reads");
            }

            Format = new AudioFormat(rate, _channels);
            var comments = VorbisComments.Read(NextHeader(3));
            var setup = new VorbisBits(NextHeader(5));
            _books = new VorbisCodebook[setup.Read(8) + 1];
            for (var book = 0; book < _books.Length; book++)
            {
                _books[book] = VorbisCodebook.Read(ref setup);
            }

            for (var times = setup.Read(6) + 1; times > 0; times--)
            {
                if (setup.Read(16) != 0)
                {
                    throw Damaged("its setup has a time transform");
                }
            }

            _floors = new VorbisFloor[setup.Read(6) + 1];
            for (var floor = 0; floor < _floors.Length; floor++)
            {
                _floors[floor] = setup.Read(16) switch
                {
                    0 => new VorbisFloor0(ref setup, _books, _channels),
                    1 => new VorbisFloor1(ref setup, _books, _channels),
                    _ => throw Damaged("a floor is of a type there is not"),
                };
            }

            _residues = new VorbisResidue[setup.Read(6) + 1];
            for (var residue = 0; residue < _residues.Length; residue++)
            {
                var type = (int)setup.Read(16);
                _residues[residue] = type <= 2
                    ? new VorbisResidue(ref setup, type, _books, _channels, _longBlock / 2)
                    : throw Damaged("a residue is of a type there is not");
            }

            _mappings = new VorbisMapping[setup.Read(6) + 1];
            for (var mapping = 0; mapping < _mappings.Length; mapping++)
            {
                _mappings[mapping] = ReadMapping(ref setup);
            }

            var modes = (int)setup.Read(6) + 1;
            _modeLong = new bool[modes];
            _modeMappings = new int[modes];
            for (var mode = 0; mode < modes; mode++)
            {
                _modeLong[mode] = setup.ReadFlag();
                if (setup.Read(16) != 0 || setup.Read(16) != 0)
                {
                    throw Damaged("a mode is of a window or transform there is not");
                }

                _modeMappings[mode] = (int)setup.Read(8);
                if (_modeMappings[mode] >= _mappings.Length)
                {
                    throw Damaged("a mode names a mapping there is not");
                }
            }

            if (!setup.ReadFlag() || setup.IsEnded)
            {
                throw Damaged("its setup does not end as one");
            }

            _modeBits = VorbisCodebook.ILog(modes - 1);
            _audioPage = _ogg.NextPage;
            _shortTransform = new VorbisTransform(_shortBlock);
            _longTransform = _shortBlock == _longBlock ? _shortTransform : new VorbisTransform(_longBlock);
            _shortSlope = SlopeOf(_shortBlock / 2);
            _longSlope = SlopeOf(_longBlock / 2);
            _speakerOrder = VorbisChannels.SpeakerIndices(_channels);
            _spectra = NewChannels(_longBlock / 2);
            _previous = NewChannels(_longBlock);
            _current = NewChannels(_longBlock);
            _floorUsed = new bool[_channels];
            _noResidue = new bool[_channels];
            _ready = new DecodedSamples(_channels, _longBlock);

            // The first sample's place in the stream: what comes before 0 is not the sound's; a stream that begins
            // later begins there.
            Restart(_audioPage);
            if (!PlaceReady())
            {
                throw Damaged("it has no audio its pages place");
            }

            _start = Math.Max(0, _ready.At);
            var last = LastGranule();
            Frames = Math.Max(0, last - _start);
            Loop = comments.LoopWithin(Frames);
            Tags = comments.Tags;
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

    public string Codec => "Vorbis";

    public SoundTags Tags { get; }

    /// <summary>Whether <paramref name="packet"/>, a first one, is Vorbis's identification.</summary>
    public static bool IsVorbis(ReadOnlySpan<byte> packet) => packet.Length >= 7 && packet[0] == 1 && packet[1..].StartsWith("vorbis"u8);

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

    // From a page whose granule is enough before the one sought; the samples before it, decoded, passed by.
    public void Seek(long frame)
    {
        if (frame < 0 || frame > Frames)
        {
            throw new InvalidDataException($"The Vorbis stream has no frame {frame}.");
        }

        var sample = _start + frame;
        var pages = _ogg.Pages;
        var after = _audioPage;
        for (var at = pages.Count - 1; at >= _audioPage; at--)
        {
            if (pages[at].Granule >= 0 && pages[at].Granule <= sample - SeekMargin * _longBlock)
            {
                after = at + 1;
                break;
            }
        }

        // From one that a packet begins and ends on, before the last: its own granule places what is decoded, which
        // the last's, that may cut the end, would not.
        var page = _audioPage;
        for (var at = Math.Min(after, pages.Count - 2); at > _audioPage; at--)
        {
            if (!pages[at].IsContinued && pages[at].Granule >= 0)
            {
                page = at;
                break;
            }
        }

        Restart(page);
        _position = sample;
    }

    public void Dispose() => _ogg.Dispose();

    private ReadOnlySpan<byte> NextHeader(int type)
    {
        if (!_ogg.TryReadPacket(out var packet, out _, out _) || packet.Length < 7 || packet[0] != type ||
            !packet[1..].StartsWith("vorbis"u8))
        {
            throw Damaged("its headers are not in their order");
        }

        return packet[7..];
    }

    private VorbisMapping ReadMapping(ref VorbisBits setup)
    {
        if (setup.Read(16) != 0)
        {
            throw Damaged("a mapping is of a type there is not");
        }

        var submaps = setup.ReadFlag() ? (int)setup.Read(4) + 1 : 1;
        var steps = setup.ReadFlag() ? (int)setup.Read(8) + 1 : 0;
        var magnitudes = new int[steps];
        var angles = new int[steps];
        var channelBits = VorbisCodebook.ILog(_channels - 1);
        for (var step = 0; step < steps; step++)
        {
            magnitudes[step] = (int)setup.Read(channelBits);
            angles[step] = (int)setup.Read(channelBits);
            if (magnitudes[step] == angles[step] || magnitudes[step] >= _channels || angles[step] >= _channels)
            {
                throw Damaged("a mapping couples channels there are not");
            }
        }

        if (setup.Read(2) != 0)
        {
            throw Damaged("a mapping's reserved field is not zero");
        }

        var mux = new int[_channels];
        if (submaps > 1)
        {
            for (var channel = 0; channel < _channels; channel++)
            {
                mux[channel] = (int)setup.Read(4);
                if (mux[channel] >= submaps)
                {
                    throw Damaged("a channel is of a submap there is not");
                }
            }
        }

        var floors = new int[submaps];
        var residues = new int[submaps];
        for (var submap = 0; submap < submaps; submap++)
        {
            setup.Read(8);
            floors[submap] = (int)setup.Read(8);
            residues[submap] = (int)setup.Read(8);
            if (floors[submap] >= _floors.Length || residues[submap] >= _residues.Length)
            {
                throw Damaged("a submap names a floor or residue there is not");
            }
        }

        var channels = Enumerable.Range(0, submaps)
            .Select(submap => Enumerable.Range(0, _channels).Where(channel => mux[channel] == submap).ToArray()).ToArray();
        return new VorbisMapping
        {
            Magnitudes = magnitudes,
            Angles = angles,
            Mux = mux,
            SubmapFloors = floors,
            SubmapResidues = residues,
            SubmapChannels = channels,
            // Filled with the spectra when they are made: the same arrays for every packet.
            SubmapVectors = [.. channels.Select(list => new float[list.Length][])],
            SubmapSkipped = [.. channels.Select(list => new bool[list.Length])],
        };
    }

    // Decoding begins at the first packet begun on a page, with no block before to overlap.
    private void Restart(int page)
    {
        _ogg.SeekToPage(page);
        _previousBlock = 0;
        _ready.Clear();
        _isFromStart = page == _audioPage;
    }

    // Decodes until the samples decoded are placed by a granule; false at the end.
    private bool PlaceReady()
    {
        while (!_ready.IsPlaced)
        {
            if (!DecodeNext())
            {
                return false;
            }
        }

        return true;
    }

    // The next packet's samples after those ready; false past the last packet.
    private bool DecodeNext()
    {
        if (!_ogg.TryReadPacket(out var packet, out var granule, out var afterLoss))
        {
            return false;
        }

        if (afterLoss)
        {
            // Read up to the loss, none are ready; those after it are placed again by the next granule.
            _previousBlock = 0;
            _ready.Clear();
            _isFromStart = false;
        }

        DecodePacket(packet);
        var page = _ogg.NextPage - 1;
        var isLast = _ogg.Pages[page].IsLast;
        if (granule < 0 || _ready.IsPlaced && isLast)
        {
            return true;
        }

        // The last sample of this packet's is the granule's: those ready end there. The last page's may say fewer
        // than its packets have, which cuts the stream's end (A.2); read from the stream's start with none placed
        // before, they begin at 0. After a page lost just before it, they are placed as if none were cut: up to what
        // was, early.
        _ready.Place(isLast && _isFromStart && granule < _ready.Count ? 0 : granule - _ready.Count);
        return true;
    }

    // 4.3: the packet's samples after those ready; how many, 0 for the first after a restart or one not audio.
    private int DecodePacket(ReadOnlySpan<byte> packet)
    {
        var bits = new VorbisBits(packet);
        if (bits.ReadFlag())
        {
            return 0;
        }

        var mode = (int)bits.Read(_modeBits);
        if (mode >= _modeLong.Length)
        {
            return 0;
        }

        var isLong = _modeLong[mode];
        var block = isLong ? _longBlock : _shortBlock;
        var previousLong = isLong && bits.ReadFlag();
        var nextLong = isLong && bits.ReadFlag();
        if (bits.IsEnded)
        {
            return 0;
        }

        var mapping = _mappings[_modeMappings[mode]];
        var half = block / 2;
        for (var channel = 0; channel < _channels; channel++)
        {
            var floor = _floors[mapping.SubmapFloors[mapping.Mux[channel]]];
            _floorUsed[channel] = floor.Read(ref bits, _books, channel);
            _noResidue[channel] = !_floorUsed[channel];
        }

        // The packet's end within the floors: all its channels silent.
        if (bits.IsEnded)
        {
            Array.Clear(_floorUsed);
            Array.Fill(_noResidue, true);
        }

        for (var step = 0; step < mapping.Magnitudes.Length; step++)
        {
            var magnitude = mapping.Magnitudes[step];
            var angle = mapping.Angles[step];
            if (!_noResidue[magnitude] || !_noResidue[angle])
            {
                _noResidue[magnitude] = _noResidue[angle] = false;
            }
        }

        for (var submap = 0; submap < mapping.SubmapChannels.Length; submap++)
        {
            var channels = mapping.SubmapChannels[submap];
            var vectors = mapping.SubmapVectors[submap];
            var skipped = mapping.SubmapSkipped[submap];
            for (var at = 0; at < channels.Length; at++)
            {
                vectors[at] = _spectra[channels[at]];
                skipped[at] = _noResidue[channels[at]];
            }

            _residues[mapping.SubmapResidues[submap]].Decode(ref bits, _books, vectors, skipped, channels.Length, half);
        }

        // 4.3.5, the last step first.
        for (var step = mapping.Magnitudes.Length - 1; step >= 0; step--)
        {
            var magnitudes = _spectra[mapping.Magnitudes[step]];
            var angles = _spectra[mapping.Angles[step]];
            for (var index = 0; index < half; index++)
            {
                var m = magnitudes[index];
                var a = angles[index];
                if (m > 0)
                {
                    (magnitudes[index], angles[index]) = a > 0 ? (m, m - a) : (m + a, m);
                }
                else
                {
                    (magnitudes[index], angles[index]) = a > 0 ? (m, m + a) : (m - a, m);
                }
            }
        }

        var transform = isLong ? _longTransform : _shortTransform;
        for (var channel = 0; channel < _channels; channel++)
        {
            var spectrum = _spectra[channel].AsSpan(0, half);
            if (_floorUsed[channel])
            {
                _floors[mapping.SubmapFloors[mapping.Mux[channel]]].Apply(channel, spectrum);
            }
            else
            {
                spectrum.Clear();
            }

            var samples = _current[channel].AsSpan(0, block);
            transform.Inverse(spectrum, samples);
            Window(samples, isLong, previousLong, nextLong);
        }

        var count = _previousBlock == 0 ? 0 : _previousBlock / 4 + block / 4;
        if (count > 0)
        {
            Overlap(block, count);
        }

        (_previous, _current) = (_current, _previous);
        _previousBlock = block;
        return count;
    }

    // 4.3.1: zero before the left slope, the slope, one, the right slope, zero after; a long block's slope beside a
    // short one is the short one's.
    private void Window(Span<float> samples, bool isLong, bool previousLong, bool nextLong)
    {
        var block = samples.Length;
        var leftHalf = isLong && !previousLong ? _shortBlock / 2 : block / 2;
        var rightHalf = isLong && !nextLong ? _shortBlock / 2 : block / 2;
        var leftStart = block / 4 - leftHalf / 2;
        var rightStart = block * 3 / 4 - rightHalf / 2;
        var left = leftHalf == _longBlock / 2 ? _longSlope : _shortSlope;
        var right = rightHalf == _longBlock / 2 ? _longSlope : _shortSlope;
        samples[..leftStart].Clear();
        for (var index = 0; index < leftHalf; index++)
        {
            samples[leftStart + index] *= left[index];
        }

        for (var index = 0; index < rightHalf; index++)
        {
            samples[rightStart + index] *= right[rightHalf - 1 - index];
        }

        samples[(rightStart + rightHalf)..].Clear();
    }

    // 4.3.8: from the previous block's middle to this one's, the previous block's right part and this one's left
    // added; after those ready, in the speakers' order.
    private void Overlap(int block, int count)
    {
        var previousBlock = _previousBlock;
        var shift = previousBlock / 4 - block / 4;
        var target = _ready.Reserve(count);
        for (var channel = 0; channel < _channels; channel++)
        {
            var previous = _previous[channel];
            var current = _current[channel];
            var output = _speakerOrder[channel];
            for (var index = 0; index < count; index++)
            {
                var atPrevious = previousBlock / 2 + index;
                var atCurrent = index - shift;
                var value = (atPrevious < previousBlock ? previous[atPrevious] : 0) + (atCurrent >= 0 ? current[atCurrent] : 0);
                target[index * _channels + output] = value;
            }
        }

        _ready.Add(count);
    }

    // Of the last page whole: a damaged one's could say any length.
    private long LastGranule()
    {
        var pages = _ogg.Pages;
        for (var at = pages.Count - 1; at >= 0; at--)
        {
            if (pages[at].Granule >= 0 && _ogg.IsWhole(at))
            {
                return pages[at].Granule;
            }
        }

        return 0;
    }

    private float[][] NewChannels(int length) => [.. Enumerable.Range(0, _channels).Select(_ => new float[length])];

    // sin(pi/2 sin^2((i + 1/2) / n * pi/2)), the rising half of the Vorbis window, of n values.
    private static float[] SlopeOf(int length) =>
        [.. Enumerable.Range(0, length).Select(index =>
            (float)Math.Sin(Math.PI / 2 * Math.Pow(Math.Sin((index + 0.5) / length * Math.PI / 2), 2)))];

    private static InvalidDataException Damaged(string how) => new($"The Vorbis stream is damaged: {how}.");
}
