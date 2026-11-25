using Adamantium.Pulsar.Decoding;

namespace Adamantium.Pulsar;

/// <summary>A sound file on the disk, or in a part of a file. It is open only while some of it is read: it may be replaced
/// while it plays, which cuts off the voices of the old one.</summary>
public sealed class SoundFileStorage : ISoundStorage
{
    private readonly string _path;
    private readonly long _offset;
    private readonly long _length;

    public SoundFileStorage(string path)
        : this(path, 0, -1)
    {
    }

    /// <param name="length">-1 for the rest of the file.</param>
    public SoundFileStorage(string path, long offset, long length)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        _path = path;
        _offset = offset;
        _length = length;
    }

    public Stream Open() => new ReopenedFileStream(_path, _offset, _length);
}
