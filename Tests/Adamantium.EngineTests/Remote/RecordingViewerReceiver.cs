using System.Collections.Concurrent;
using Adamantium.Multiverse.Remote;

namespace Adamantium.EngineTests.Remote;

/// <summary>A viewer end that writes down what its host told it, one line a message, in order.</summary>
public class RecordingViewerReceiver : IUniverseViewerReceiver
{
    public BlockingCollection<string> Messages { get; } = new();

    public RemoteSurface LastSurface { get; private set; }

    public void Greet(RemoteHello hello) => Messages.Add($"Greet {hello.ProcessId} {hello.DeviceUuid}");

    public void ShowSurface(RemoteSurface surface)
    {
        LastSurface = surface;
        Messages.Add($"ShowSurface {surface.Width}x{surface.Height} {surface.Buffers.Length}");
    }

    public void ShowFrame(ulong frame) => Messages.Add($"ShowFrame {frame}");

    public void EndUniverse() => Messages.Add("EndUniverse");

    public void LoseHost() => Messages.Add("LoseHost");
}
