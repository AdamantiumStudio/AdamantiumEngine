using System;
using System.IO;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>A section of an asset file, from its beginning to its end; the file is closed with it.</summary>
internal sealed class AssetSectionStream : Stream
{
    private readonly Stream file;
    private readonly long offset;
    private long position;

    public AssetSectionStream(Stream file, long offset, long length)
    {
        this.file = file;
        this.offset = offset;
        Length = length;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

    public override long Position
    {
        get => position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            position = value;
        }
    }

    public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));

    public override int Read(Span<byte> buffer)
    {
        var left = Math.Max(0, Length - position);
        if (left < buffer.Length)
        {
            buffer = buffer[..(int)left];
        }

        file.Position = offset + position;
        var read = file.Read(buffer);
        position += read;
        return read;
    }

    public override long Seek(long to, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Current => position + to,
        SeekOrigin.End => Length + to,
        _ => to
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            file.Dispose();
        }

        base.Dispose(disposing);
    }
}
