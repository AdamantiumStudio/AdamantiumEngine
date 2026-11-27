using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>The packets of one logical stream of an Ogg file (RFC 3533): the first one that begins by a codec it knows,
/// to its last page; a chained stream's later links are not read. Its pages are found when it opens, by their headers
/// alone; a page's checksum is checked as it is read, and a damaged page is passed by, the packets it held lost. Of a
/// stream read only forward - a station's - its pages are read as they come, with no index and no seeking, and the first
/// page of the chain's next link ends it, left for the next link to begin by.</summary>
internal sealed class OggStream : IDisposable
{
    private const int HeaderLength = 27;
    private const int LongestPage = HeaderLength + 255 + 255 * 255;
    // How far past bytes that are no page the next is looked for.
    private const int LongestSearch = 1 << 16;
    private static readonly uint[] Checksums = ChecksumsOf();

    private readonly Stream _stream;
    // Of a file; null of a stream read forward, which has the reader.
    private readonly OggPage[] _pages;
    private readonly ForwardReader _reader;
    private readonly byte[] _page = new byte[LongestPage];
    // The page read last.
    private OggPage _current;
    // For pages checked apart from the one being read; made when one first is.
    private byte[] _check;
    private byte[] _packet = new byte[1 << 16];
    private int _packetLength;
    private int _next;
    private int _segments;
    private int _segment;
    private int _bodyAt;
    private bool _isSkipping;
    private bool _isLost;
    // Of the page read last, its number; -1 where none was, or it was damaged.
    private long _sequence = -1;

    private OggStream(Stream stream, OggPage[] pages, uint serial)
    {
        _stream = stream;
        _pages = pages;
        Serial = serial;
    }

    private OggStream(ForwardReader reader, uint serial)
    {
        _reader = reader;
        Serial = serial;
    }

    public uint Serial { get; }

    /// <summary>Read only forward, as it comes: of no length known, not sought in.</summary>
    public bool IsForward => _reader != null;

    /// <summary>Of a stream read forward: the next link of the chain began, after this one's last page.</summary>
    public bool HasNextLink { get; private set; }

    /// <exception cref="InvalidOperationException">It is read forward, and has no index of its pages.</exception>
    public IReadOnlyList<OggPage> Pages =>
        _pages ?? throw new InvalidOperationException("An Ogg stream read forward has no index of its pages.");

    /// <summary>The page the packet read last ended on.</summary>
    public OggPage CurrentPage => _current;

    /// <summary>The page read after the one being read; where the packets after headers that end their page begin.
    /// </summary>
    public int NextPage => _next;

    /// <summary>The logical stream of <paramref name="stream"/> whose first packet <paramref name="isKnown"/> takes, from
    /// the stream's position.</summary>
    /// <exception cref="InvalidDataException">It is no Ogg file, or has no such stream.</exception>
    public static OggStream Open(Stream stream, Func<ReadOnlySpan<byte>, bool> isKnown) =>
        TryIndex(stream, isKnown, out var pages, out var serial)
            ? new OggStream(stream, pages, serial)
            : throw new InvalidDataException("The Ogg file has no stream of a sound it can read.");

    /// <summary>The logical stream of <paramref name="reader"/>'s whose first page begins by a packet
    /// <paramref name="isKnown"/> takes, read forward from there; the pages before it, of no such stream, are passed by.
    /// Its first page is left for its first packet.</summary>
    /// <exception cref="InvalidDataException">No such stream begins.</exception>
    public static OggStream OpenForward(ForwardReader reader, Func<ReadOnlySpan<byte>, bool> isKnown)
    {
        for (var passed = 0L; passed <= LongestSearch;)
        {
            if (!TryPeekPage(reader, out var page, out var serial))
            {
                break;
            }

            var length = page.HeaderLength + page.BodyLength;
            if (page.IsFirst && isKnown(reader.Ahead(length)[page.HeaderLength..]))
            {
                return new OggStream(reader, serial);
            }

            reader.Take(length);
            passed += length;
        }

        throw new InvalidDataException("The Ogg stream has no stream of a sound it can read.");
    }

    private static bool TryIndex(Stream stream, Func<ReadOnlySpan<byte>, bool> isKnown, out OggPage[] found,
        out uint chosen)
    {
        var header = new byte[HeaderLength + 255];
        var pages = new List<OggPage>();
        uint? serial = null;
        var at = stream.Position;
        var previous = at - 1;
        // Where a page's end is no page, its header may be damaged, its length too: the next is looked for from just
        // after where it began.
        while (TryHeaderAt(stream, at, header, out var page, out var pageSerial) ||
            FindPage(stream, previous + 1, header, out at, out page, out pageSerial))
        {
            previous = page.At;
            if (serial == null && page.IsFirst && IsKnownStream(stream, page, isKnown))
            {
                serial = pageSerial;
            }

            if (serial == pageSerial)
            {
                pages.Add(page);
                if (page.IsLast)
                {
                    break;
                }
            }
            else if (serial == null && pages.Count == 0 && !page.IsFirst)
            {
                // Past the first pages, which begin every stream, with none of a codec it knows.
                break;
            }

            at = page.End;
        }

        found = [.. pages];
        chosen = serial ?? 0;
        return serial != null;
    }

    /// <summary>The next packet read is the first to begin on page <paramref name="page"/>.</summary>
    /// <exception cref="InvalidOperationException">It is read forward, and the page is not the next.</exception>
    public void SeekToPage(int page)
    {
        if (IsForward && page != _next)
        {
            throw new InvalidOperationException("An Ogg stream read forward is not sought in.");
        }

        _next = page;
        _segments = _segment = 0;
        _packetLength = 0;
        _isSkipping = false;
        _isLost = false;
        // Pages missing between it and the one before are a loss as any.
        if (!IsForward)
        {
            _sequence = page > 0 && page <= _pages.Length ? _pages[page - 1].Sequence : -1;
        }
    }

    /// <summary>The next whole packet, with the granule its page says where it is the last to end on it, -1 otherwise;
    /// whether packets were lost before it. False past the last.</summary>
    public bool TryReadPacket(out ReadOnlySpan<byte> packet, out long granule, out bool afterLoss)
    {
        while (true)
        {
            if (_segment == _segments && !TryLoadPage())
            {
                packet = default;
                granule = -1;
                afterLoss = _isLost;
                return false;
            }

            var length = _page[HeaderLength + _segment];
            if (!_isSkipping)
            {
                Append(_page.AsSpan(_bodyAt, length));
            }

            _bodyAt += length;
            _segment++;
            if (length == 255)
            {
                continue;
            }

            if (_isSkipping)
            {
                _isSkipping = false;
                continue;
            }

            packet = _packet.AsSpan(0, _packetLength);
            _packetLength = 0;
            granule = EndsNoOther() ? _current.Granule : -1;
            afterLoss = _isLost;
            _isLost = false;
            return true;
        }
    }

    /// <summary>Whether page <paramref name="page"/> is as its checksum says; the packet being read is left as it is,
    /// the stream's position not.</summary>
    public bool IsWhole(int page) => IsWholeAt(_stream, Pages[page], _check ??= new byte[LongestPage]);

    private static bool IsWholeAt(Stream stream, OggPage page, byte[] buffer = null)
    {
        var length = page.HeaderLength + page.BodyLength;
        var bytes = (buffer ?? new byte[length]).AsSpan(0, length);
        stream.Position = page.At;
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[22..]) == ChecksumOf(bytes);
    }

    /// <summary>Of page <paramref name="page"/>'s body, from <paramref name="from"/>, as much as there is of
    /// <paramref name="bytes"/>; how much was read.</summary>
    public int ReadBody(int page, long from, Span<byte> bytes)
    {
        var info = Pages[page];
        var count = (int)Math.Clamp(info.BodyLength - from, 0, bytes.Length);
        _stream.Position = info.At + info.HeaderLength + from;
        return _stream.ReadAtLeast(bytes[..count], count, throwOnEndOfStream: false);
    }

    // Of a stream read forward, the reader is the chain's, which goes on to its next link.
    public void Dispose()
    {
        if (!IsForward)
        {
            _stream.Dispose();
        }
    }

    // The next page into the buffer, its checksum checked; a damaged one is passed by. False past the last.
    private bool TryLoadPage()
    {
        if (IsForward)
        {
            return TryLoadNextPage();
        }

        while (_next < _pages.Length)
        {
            var page = _pages[_next++];
            var bytes = _page.AsSpan(0, page.HeaderLength + page.BodyLength);
            _stream.Position = page.At;
            if (_stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) < bytes.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[22..]) != ChecksumOf(bytes))
            {
                Lose();
                continue;
            }

            Accept(page);
            return true;
        }

        return false;
    }

    // Of a stream read forward: the next page of this stream as it comes; another stream's passed by, the first of the
    // chain's next link left where it is. False at the end of the stream or of the link.
    private bool TryLoadNextPage()
    {
        while (TryPeekPage(_reader, out var page, out var serial))
        {
            // A first page after this one's pages is the next link's, whatever its number.
            if (page.IsFirst && (serial != Serial || _next > 0))
            {
                HasNextLink = true;
                return false;
            }

            var length = page.HeaderLength + page.BodyLength;
            if (serial != Serial)
            {
                _reader.Take(length);
                continue;
            }

            // Found whole, by its checksum: a damaged one was passed by as bytes of no page, a gap in the numbers.
            _reader.Ahead(length).CopyTo(_page);
            _reader.Take(length);
            _next++;
            Accept(page);
            return true;
        }

        return false;
    }

    // A damaged page: the packet being read is lost with it.
    private void Lose()
    {
        _packetLength = 0;
        _isSkipping = false;
        _isLost = true;
        _sequence = -1;
    }

    // The page in the buffer, whole, is the one read from.
    private void Accept(OggPage page)
    {
        // Pages between it and the one before that are not here at all - their headers too damaged to be found - are
        // lost as the damaged ones are.
        if (_sequence >= 0 && page.Sequence != (uint)(_sequence + 1))
        {
            _packetLength = 0;
            _isSkipping = false;
            _isLost = true;
        }

        _sequence = page.Sequence;

        // A packet it goes on with that was not begun - a seek's first page, or one after a page lost - is passed by;
        // one left unended by a page that went on to no other is lost.
        if (page.IsContinued)
        {
            _isSkipping |= _packetLength == 0;
        }
        else
        {
            _isLost |= _packetLength > 0;
            _packetLength = 0;
            _isSkipping = false;
        }

        _current = page;
        _segments = _page[HeaderLength - 1];
        _segment = 0;
        _bodyAt = page.HeaderLength;
    }

    // Whether no packet after the one just ended ends on its page.
    private bool EndsNoOther()
    {
        for (var segment = _segment; segment < _segments; segment++)
        {
            if (_page[HeaderLength + segment] != 255)
            {
                return false;
            }
        }

        return true;
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        if (_packetLength + bytes.Length > _packet.Length)
        {
            Array.Resize(ref _packet, Math.Max(_packet.Length * 2, _packetLength + bytes.Length));
        }

        bytes.CopyTo(_packet.AsSpan(_packetLength));
        _packetLength += bytes.Length;
    }

    private static bool IsKnownStream(Stream stream, OggPage page, Func<ReadOnlySpan<byte>, bool> isKnown)
    {
        // A first page holds its stream's first packet alone, which is short.
        var body = new byte[Math.Min(page.BodyLength, 64)];
        stream.Position = page.At + page.HeaderLength;
        return stream.ReadAtLeast(body, body.Length, throwOnEndOfStream: false) == body.Length && isKnown(body);
    }

    private static bool TryHeaderAt(Stream stream, long at, byte[] header, out OggPage page, out uint serial)
    {
        page = default;
        serial = 0;
        stream.Position = at;
        if (stream.ReadAtLeast(header.AsSpan(0, HeaderLength), HeaderLength, throwOnEndOfStream: false) < HeaderLength ||
            !header.AsSpan().StartsWith("OggS"u8) || header[4] != 0)
        {
            return false;
        }

        var segments = header[26];
        if (stream.ReadAtLeast(header.AsSpan(HeaderLength, segments), segments, throwOnEndOfStream: false) < segments)
        {
            return false;
        }

        page = PageOf(header, at, out serial);
        return page.End <= stream.Length;
    }

    // The page whose header - its segment table too - <paramref name="header"/> begins by, of a stream checked to be a
    // page's by its capture pattern and version.
    private static OggPage PageOf(ReadOnlySpan<byte> header, long at, out uint serial)
    {
        var segments = header[26];
        var body = 0;
        foreach (var segment in header.Slice(HeaderLength, segments))
        {
            body += segment;
        }

        serial = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
        return new OggPage(at, HeaderLength + segments, body, BinaryPrimitives.ReadInt64LittleEndian(header[6..]),
            header[5], BinaryPrimitives.ReadUInt32LittleEndian(header[18..]));
    }

    // Of a stream read forward: the next whole page, as its checksum says, all of it ahead and none taken; the bytes
    // before it that are none - damage, a page cut short, the middle of one the station began in - are taken. False at
    // the end, or past as many bytes of no page as are looked through.
    private static bool TryPeekPage(ForwardReader reader, out OggPage page, out uint serial)
    {
        for (var passed = 0; passed <= LongestSearch; passed++)
        {
            if (!reader.Has(HeaderLength))
            {
                break;
            }

            var header = reader.Ahead(HeaderLength);
            if (header.StartsWith("OggS"u8) && header[4] == 0 && reader.Has(HeaderLength + header[26]))
            {
                page = PageOf(reader.Ahead(HeaderLength + header[26]), 0, out serial);
                var length = page.HeaderLength + page.BodyLength;
                if (reader.Has(length) &&
                    BinaryPrimitives.ReadUInt32LittleEndian(reader.Ahead(length)[22..]) == ChecksumOf(reader.Ahead(length)))
                {
                    return true;
                }
            }

            reader.Take(1);
        }

        page = default;
        serial = 0;
        return false;
    }

    // The next page after bytes that are none: damage, or what a stream ends by. Found by its capture pattern, it is
    // taken where it is whole, as its pattern may be in what is no page.
    private static bool FindPage(Stream stream, long from, byte[] header, out long at, out OggPage page, out uint serial)
    {
        var buffer = new byte[4096];
        for (at = from; at < Math.Min(stream.Length, from + LongestSearch);)
        {
            stream.Position = at;
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            var found = buffer.AsSpan(0, read).IndexOf("OggS"u8);
            if (found >= 0)
            {
                if (TryHeaderAt(stream, at + found, header, out page, out serial) && IsWholeAt(stream, page))
                {
                    at += found;
                    return true;
                }

                at += found + 1;
                continue;
            }

            at += Math.Max(1, read - 3);
        }

        page = default;
        serial = 0;
        return false;
    }

    // RFC 3533's: of the page with its checksum's field as zeros; polynomial 0x04C11DB7, from 0, the highest bit
    // first.
    private static uint ChecksumOf(ReadOnlySpan<byte> page)
    {
        var sum = 0u;
        for (var index = 0; index < page.Length; index++)
        {
            var value = index is >= 22 and < 26 ? (byte)0 : page[index];
            sum = sum << 8 ^ Checksums[(sum >> 24 ^ value) & 0xFF];
        }

        return sum;
    }

    private static uint[] ChecksumsOf()
    {
        var table = new uint[256];
        for (var value = 0u; value < 256; value++)
        {
            var sum = value << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                sum = (sum & 0x80000000) != 0 ? sum << 1 ^ 0x04C11DB7 : sum << 1;
            }

            table[value] = sum;
        }

        return table;
    }
}
