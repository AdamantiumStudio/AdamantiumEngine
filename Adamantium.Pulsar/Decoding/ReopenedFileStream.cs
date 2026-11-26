namespace Adamantium.Pulsar.Decoding;

/// <summary>A part of a file, open only while it is read: Windows lets no one replace an open file, and music plays for
/// minutes. One replaced meanwhile is read no more.</summary>
internal sealed class ReopenedFileStream : Stream
{
    private readonly string _path;
    private readonly long _offset;
    private readonly long _fileLength;
    private readonly DateTime _written;
    private long _position;

    /// <param name="length">-1 for the rest of the file.</param>
    public ReopenedFileStream(string path, long offset, long length)
    {
        _path = path;
        _offset = offset;
        using var file = OpenFile();
        _fileLength = file.Length;
        _written = File.GetLastWriteTimeUtc(file.SafeFileHandle);
        Length = length >= 0 ? length : _fileLength - offset;
        if (offset < 0 || Length < 0 || offset > _fileLength - Length)
        {
            throw new InvalidDataException($"{path} ends before the sound in it does.");
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        buffer = buffer[..(int)Math.Min(buffer.Length, Math.Max(0, Length - _position))];
        if (buffer.IsEmpty)
        {
            return 0;
        }

        using var file = OpenFile();
        if (file.Length != _fileLength || File.GetLastWriteTimeUtc(file.SafeFileHandle) != _written)
        {
            throw new IOException($"{_path} was written anew while its sound played.");
        }

        file.Position = _offset + _position;
        var read = file.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => Length + offset,
        _ => offset,
    };

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Unbuffered: what is asked for is large.
    private FileStream OpenFile() =>
        new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
}
