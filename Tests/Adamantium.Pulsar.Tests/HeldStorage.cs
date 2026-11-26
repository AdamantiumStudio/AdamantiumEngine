namespace Adamantium.Pulsar.Tests;

/// <summary>A sound file in memory whose first opening waits to be let go.</summary>
internal sealed class HeldStorage(byte[] file) : ISoundStorage
{
    private int _opened;

    public ManualResetEventSlim Opened { get; } = new();

    public ManualResetEventSlim Release { get; } = new();

    public Stream Open()
    {
        if (Interlocked.Increment(ref _opened) == 1)
        {
            Opened.Set();
            Release.Wait();
        }

        return new MemoryStream(file);
    }
}
