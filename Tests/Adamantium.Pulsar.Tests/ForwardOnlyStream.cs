namespace Adamantium.Pulsar.Tests;

/// <summary>Bytes that come as a station's do: only forward, a few at a time, with no length and no seeking.</summary>
internal sealed class ForwardOnlyStream(byte[] bytes, int mostAtOnce = 1000) : Stream
{
    private int _at;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var taken = Math.Min(Math.Min(count, mostAtOnce), bytes.Length - _at);
        bytes.AsSpan(_at, taken).CopyTo(buffer.AsSpan(offset));
        _at += taken;
        return taken;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
