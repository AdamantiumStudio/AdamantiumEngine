namespace Adamantium.Pulsar.Decoding;

/// <summary>FLAC in Ogg as the native FLAC file it holds: the bodies of its pages, one after the other, from the "fLaC"
/// of its first packet on. Its packets are the native file's metadata blocks and frames, in order, so a FLAC decoder
/// reads it, seeks in it and finds frames after damage as in any; the pages' checksums are not checked, the frames' are.
/// Read-only; its file is disposed with it.</summary>
internal sealed class OggFlacStream : Stream
{
    // The first packet's mapping header, before "fLaC": 0x7F, "FLAC", the mapping's version, the headers' count.
    private const int MappingLength = 9;

    private readonly OggStream _ogg;
    // By page, where its body begins in what it reads as; one more for the end.
    private readonly long[] _starts;
    private long _position;

    public OggFlacStream(OggStream ogg)
    {
        _ogg = ogg;
        var pages = ogg.Pages;
        _starts = new long[pages.Count + 1];
        for (var page = 0; page < pages.Count; page++)
        {
            _starts[page + 1] = _starts[page] + pages[page].BodyLength;
        }

        Length = Math.Max(0, _starts[^1] - MappingLength);
    }

    /// <summary>Whether <paramref name="packet"/>, a first one, is FLAC's mapping header, of version 1.</summary>
    public static bool IsFlac(ReadOnlySpan<byte> packet) =>
        packet.Length >= MappingLength + 4 && packet[0] == 0x7F && packet[1..].StartsWith("FLAC"u8) && packet[5] == 1 &&
        packet[MappingLength..].StartsWith("fLaC"u8);

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(Span<byte> buffer)
    {
        var done = 0;
        while (done < buffer.Length && _position < Length)
        {
            var at = _position + MappingLength;
            var page = Array.BinarySearch(_starts, at);
            page = page >= 0 ? page : ~page - 1;
            // Pages of no body share their start with the next one.
            while (_starts[page + 1] <= at)
            {
                page++;
            }

            var within = at - _starts[page];
            var count = (int)Math.Min(buffer.Length - done, _starts[page + 1] - at);
            var read = _ogg.ReadBody(page, within, buffer.Slice(done, count));
            done += read;
            _position += read;
            if (read < count)
            {
                break;
            }
        }

        return done;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ogg.Dispose();
        }

        base.Dispose(disposing);
    }
}
