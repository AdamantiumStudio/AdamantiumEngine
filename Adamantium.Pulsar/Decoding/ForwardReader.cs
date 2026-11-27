namespace Adamantium.Pulsar.Decoding;

/// <summary>A stream read only forward - a station's, as it comes - with what is ahead seen before it is taken: a
/// decoder finds its frames in it without seeking.</summary>
internal sealed class ForwardReader(Stream stream) : IDisposable
{
    private const int FirstCapacity = 1 << 16;

    private byte[] _buffer = new byte[FirstCapacity];
    private int _start;
    private int _end;
    private bool _hasEnded;

    /// <summary>The bytes come to, not taken yet.</summary>
    public int Available => _end - _start;

    /// <summary>Whether <paramref name="count"/> bytes are ahead, read from the stream as far as it takes: false only at
    /// its end.</summary>
    public bool Has(int count)
    {
        while (Available < count && !_hasEnded)
        {
            if (_buffer.Length - _start < count)
            {
                // Taken bytes give their room; a run longer than the buffer makes it grow.
                var kept = Available;
                var buffer = count > _buffer.Length ? new byte[Math.Max(count, _buffer.Length * 2)] : _buffer;
                _buffer.AsSpan(_start, kept).CopyTo(buffer);
                _buffer = buffer;
                _start = 0;
                _end = kept;
            }

            var read = stream.Read(_buffer, _end, _buffer.Length - _end);
            if (read == 0)
            {
                _hasEnded = true;
            }

            _end += read;
        }

        return Available >= count;
    }

    /// <summary>The bytes ahead, as many as <see cref="Has"/> found; not taken.</summary>
    public ReadOnlySpan<byte> Ahead(int count) => _buffer.AsSpan(_start, Math.Min(count, Available));

    /// <summary>Takes <paramref name="count"/> bytes, of those ahead.</summary>
    public void Take(int count) => _start += Math.Min(count, Available);

    /// <summary>Takes <paramref name="count"/> bytes whether they came or not: false where the stream ended first.</summary>
    public bool Skip(long count)
    {
        while (count > 0)
        {
            if (!Has(1))
            {
                return false;
            }

            var taken = (int)Math.Min(count, Available);
            Take(taken);
            count -= taken;
        }

        return true;
    }

    public void Dispose() => stream.Dispose();
}
