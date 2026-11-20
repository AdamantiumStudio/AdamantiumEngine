using System.Collections.Concurrent;
using Adamantium.Multiverse.Remote;

namespace Adamantium.UniverseHost.Tests;

/// <summary>A viewer that keeps what the host tells it, for a test to wait on.</summary>
public sealed class WatchingViewer : IUniverseViewer
{
    private long _lastFrame;

    public BlockingCollection<RemoteHello> Hellos { get; } = new();

    public BlockingCollection<RemoteSurface> Surfaces { get; } = new();

    public ManualResetEventSlim Ended { get; } = new();

    /// <summary>The newest frame the host told of.</summary>
    public ulong LastFrame => (ulong)Interlocked.Read(ref _lastFrame);

    public void Greet(RemoteHello hello) => Hellos.Add(hello);

    public void ShowSurface(RemoteSurface surface) => Surfaces.Add(surface);

    public void ShowFrame(ulong frame) => Interlocked.Exchange(ref _lastFrame, (long)frame);

    public void EndUniverse() => Ended.Set();
}
