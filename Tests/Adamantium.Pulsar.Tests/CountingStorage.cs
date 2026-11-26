namespace Adamantium.Pulsar.Tests;

/// <summary>A file in memory that counts who reads it, and can be gone or slow.</summary>
internal sealed class CountingStorage(byte[] file) : ISoundStorage
{
    private readonly ManualResetEventSlim _open = new(true);
    private int _left;

    /// <summary>How many of its streams are not disposed.</summary>
    public int Left => Volatile.Read(ref _left);

    public volatile bool IsGone;

    public volatile bool FailsToClose;

    public void Block() => _open.Reset();

    public void Unblock() => _open.Set();

    public Stream Open()
    {
        if (IsGone)
        {
            throw new IOException("gone");
        }

        Interlocked.Increment(ref _left);
        return new CountedStream(this, file);
    }

    private sealed class CountedStream(CountingStorage storage, byte[] file) : MemoryStream(file, writable: false)
    {
        private bool _disposed;

        // The least of what a stream may give, as a slow disk.
        public override int Read(Span<byte> buffer)
        {
            storage._open.Wait();
            return base.Read(buffer[..Math.Min(buffer.Length, 1024)]);
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                Interlocked.Decrement(ref storage._left);
            }

            base.Dispose(disposing);
            if (storage.FailsToClose)
            {
                throw new IOException("not closed");
            }
        }
    }
}
