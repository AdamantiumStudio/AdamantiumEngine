using System.Collections.Concurrent;
using Adamantium.Multiverse.Remote;

namespace Adamantium.UniverseHost.Tests;

/// <summary>A viewer that keeps what the host tells it, for a test to wait on.</summary>
internal sealed class WatchingViewer : IUniverseViewerReceiver
{
    public BlockingCollection<RemoteHello> Hellos { get; } = new();

    public BlockingCollection<RemoteSurface> Surfaces { get; } = new();

    public BlockingCollection<ulong> Frames { get; } = new();

    public ManualResetEventSlim Ended { get; } = new();

    public void Greet(RemoteHello hello) => Hellos.Add(hello);

    public void ShowSurface(RemoteSurface surface) => Surfaces.Add(surface);

    public void ShowFrame(ulong frame) => Frames.Add(frame);

    public void EndUniverse() => Ended.Set();

    public void LoseHost() => Ended.Set();
}
