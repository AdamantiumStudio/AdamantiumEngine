namespace Adamantium.Pulsar;

/// <summary>A sound file on the disk.</summary>
public sealed class SoundFileStorage : ISoundStorage
{
    private readonly string _path;

    public SoundFileStorage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    // It may be replaced while it plays, by an import say.
    public Stream Open() => new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
