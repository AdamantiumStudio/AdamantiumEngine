using System.Collections.Concurrent;
using Adamantium.Multiverse.Remote;

namespace Adamantium.EngineTests.Remote;

/// <summary>A viewer that writes down what its host told it, one line a call, in order.</summary>
public class RecordingViewer : IUniverseViewer
{
    public BlockingCollection<string> Calls { get; } = new();

    public RemoteSurface LastSurface { get; private set; }

    public void Greet(RemoteHello hello) => Calls.Add($"Greet {hello.ProcessId}");

    public void ShowSurface(RemoteSurface surface)
    {
        LastSurface = surface;
        Calls.Add($"ShowSurface {surface.Width}x{surface.Height} {surface.Buffers.Length}");
    }

    public void ShowFrame(ulong frame) => Calls.Add($"ShowFrame {frame}");

    public void EndUniverse() => Calls.Add("EndUniverse");
}
