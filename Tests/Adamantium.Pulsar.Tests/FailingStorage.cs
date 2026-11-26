namespace Adamantium.Pulsar.Tests;

/// <summary>A sound file in memory that can be taken away.</summary>
internal sealed class FailingStorage(byte[] file) : ISoundStorage
{
    public volatile bool Fails;

    public Stream Open() => Fails ? throw new UnauthorizedAccessException("Taken away.") : new MemoryStream(file);
}
