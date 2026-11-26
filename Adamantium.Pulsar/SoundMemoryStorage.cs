namespace Adamantium.Pulsar;

/// <summary>A sound file in memory, as it is on the disk.</summary>
public sealed class SoundMemoryStorage : ISoundStorage
{
    private readonly byte[] _file;

    public SoundMemoryStorage(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
    }

    public Stream Open() => new MemoryStream(_file, writable: false);
}
