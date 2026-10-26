using System;
using System.IO;

namespace Adamantium.Imaging;

/// <summary>A file written from the start, created - or cut to nothing - only when the first byte is written to it:
/// an encoder that refuses before it writes leaves a file already there as it was.</summary>
internal sealed class FileOnFirstWrite(string path) : Stream
{
    private FileStream _file;
    private long _written;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        _file ??= new FileStream(path, FileMode.Create, FileAccess.Write);
        _file.Write(buffer);
        _written += buffer.Length;
    }

    public override void WriteByte(byte value) => Write([value]);

    public override void Flush() => _file?.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file?.Dispose();
        }

        base.Dispose(disposing);
    }
}
