using System.Text;

namespace Adamantium.Pulsar.Live;

/// <summary>A station's stream with its metadata taken out (ICY, as SHOUTcast and Icecast send it): after every
/// <c>icy-metaint</c> bytes of sound, a byte says how long the text after it is, in sixteens of bytes; the text's
/// <c>StreamTitle='...';</c> is what plays now, told when it changes. Read only forward.</summary>
internal sealed class IcyStream(Stream stream, int interval, Action<string> titleChanged) : Stream
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    private readonly byte[] _text = new byte[255 * 16];
    private readonly int _interval = interval;
    private int _untilText = interval;
    private bool _hasEnded;
    private string _title;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || _hasEnded)
        {
            return 0;
        }

        if (_untilText == 0)
        {
            ReadText();
            if (_hasEnded)
            {
                return 0;
            }

            _untilText = _interval;
        }

        var read = stream.Read(buffer[..Math.Min(buffer.Length, _untilText)]);
        _untilText -= read;
        _hasEnded = read == 0;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>The title in <paramref name="text"/>, ICY's metadata: what is between <c>StreamTitle='</c> and the next
    /// <c>';</c>, as UTF-8, else as Latin-1, which older stations write; null where it says none.</summary>
    internal static string TitleOf(ReadOnlySpan<byte> text)
    {
        var start = text.IndexOf("StreamTitle='"u8);
        if (start < 0)
        {
            return null;
        }

        var value = text[(start + "StreamTitle='"u8.Length)..];
        var end = value.IndexOf("';"u8);
        value = end >= 0 ? value[..end] : value.TrimEnd((byte)0);
        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
        }

        base.Dispose(disposing);
    }

    // The length's byte and the text it says; a text of no title, or of the one there was, changes nothing.
    private void ReadText()
    {
        Span<byte> length = stackalloc byte[1];
        if (stream.ReadAtLeast(length, 1, throwOnEndOfStream: false) < 1)
        {
            _hasEnded = true;
            return;
        }

        var text = _text.AsSpan(0, length[0] * 16);
        if (stream.ReadAtLeast(text, text.Length, throwOnEndOfStream: false) < text.Length)
        {
            _hasEnded = true;
            return;
        }

        if (TitleOf(text) is { } title && title != _title)
        {
            _title = title;
            titleChanged?.Invoke(title);
        }
    }
}
