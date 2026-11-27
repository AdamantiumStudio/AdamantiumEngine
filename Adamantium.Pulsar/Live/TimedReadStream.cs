namespace Adamantium.Pulsar.Live;

/// <summary>A network stream whose read gives up when nothing comes for so long, or when it is cancelled: a station that
/// fell silent is a drop, not a read that waits for ever.</summary>
internal sealed class TimedReadStream(Stream stream, TimeSpan timeout, CancellationToken cancellation) : Stream
{
    private CancellationTokenSource _wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
    // For reads into spans, which an asynchronous read does not take.
    private byte[] _bytes = [];

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <exception cref="IOException">Nothing came for the time it waits.</exception>
    /// <exception cref="OperationCanceledException">It was cancelled.</exception>
    public override int Read(byte[] buffer, int offset, int count) => ReadWithin(buffer.AsMemory(offset, count));

    /// <inheritdoc cref="Read(byte[], int, int)"/>
    public override int Read(Span<byte> buffer)
    {
        if (_bytes.Length < buffer.Length)
        {
            _bytes = new byte[buffer.Length];
        }

        var read = ReadWithin(_bytes.AsMemory(0, buffer.Length));
        _bytes.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wait.Dispose();
            stream.Dispose();
        }

        base.Dispose(disposing);
    }

    private int ReadWithin(Memory<byte> bytes)
    {
        cancellation.ThrowIfCancellationRequested();
        // One that gave up is not reset: a new one waits.
        if (!_wait.TryReset())
        {
            _wait.Dispose();
            _wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        }

        _wait.CancelAfter(timeout);
        try
        {
            return stream.ReadAsync(bytes, _wait.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new IOException($"Nothing came for {timeout.TotalSeconds:0} s.");
        }
    }
}
