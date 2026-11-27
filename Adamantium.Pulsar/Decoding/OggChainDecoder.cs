namespace Adamantium.Pulsar.Decoding;

/// <summary>An Ogg stream read as it comes - a station's - of Vorbis or Opus: link after link of its chain, as stations
/// begin one each song, its headers anew. A link of the same format goes on as one sound; one of another ends it.</summary>
internal sealed class OggChainDecoder : ISoundDecoder
{
    private readonly ForwardReader _reader;
    private ISoundDecoder _link;
    private OggStream _ogg;
    private bool _hasEnded;

    /// <exception cref="InvalidDataException">No stream of Vorbis or Opus begins.</exception>
    public OggChainDecoder(ForwardReader reader)
    {
        _reader = reader;
        _link = OpenLink();
        Format = _link.Format;
        Codec = _link.Codec;
    }

    public AudioFormat Format { get; }

    public long Frames => -1;

    public SoundLoop? Loop => null;

    public string Codec { get; }

    /// <summary>Of the link read now: a station says the song in each link's.</summary>
    public SoundTags Tags => _link.Tags;

    public int Read(Span<float> samples)
    {
        var channels = Format.Channels;
        var done = 0;
        while (done * channels < samples.Length && !_hasEnded)
        {
            var got = _link.Read(samples[(done * channels)..]);
            done += got;
            if (got == 0 && !NextLink())
            {
                _hasEnded = true;
            }
        }

        return done;
    }

    public void Seek(long frame) =>
        throw new InvalidDataException("An Ogg stream that comes as it plays is not sought in.");

    public void Dispose()
    {
        _link.Dispose();
        _reader.Dispose();
    }

    // The link after the one that ended, where one began and is of the same format and codec: whether there is. One of
    // another is let go, the one that ended kept.
    private bool NextLink()
    {
        if (!_ogg.HasNextLink)
        {
            return false;
        }

        ISoundDecoder next;
        try
        {
            next = OpenLink();
        }
        catch (InvalidDataException)
        {
            return false;
        }

        if (next.Format != Format || next.Codec != Codec)
        {
            next.Dispose();
            return false;
        }

        _link.Dispose();
        _link = next;
        return true;
    }

    // By its first page's body, ahead whole once the link is found: past the header of 27 bytes and its segments.
    private ISoundDecoder OpenLink()
    {
        _ogg = OggStream.OpenForward(_reader, packet => VorbisDecoder.IsVorbis(packet) || OggOpusDecoder.IsReadable(packet));
        var page = _reader.Ahead(27 + 255 + 8);
        return OggOpusDecoder.IsOpus(page[(27 + page[26])..]) ? new OggOpusDecoder(_ogg) : new VorbisDecoder(_ogg);
    }
}
