using MessagePack;

namespace Adamantium.Multiverse.Remote;

/// <summary>The viewer as its host tells it: each notice a message on the link.</summary>
internal sealed class UniverseViewerProxy(RemoteLink link) : IUniverseViewer
{
    public void Greet(RemoteHello hello) =>
        link.Tell((int)ViewerMethod.Greet,
            (ref MessagePackWriter writer) => MessagePackSerializer.Serialize(ref writer, hello, RemoteLink.Options));

    public void ShowSurface(RemoteSurface surface) =>
        link.Tell((int)ViewerMethod.ShowSurface,
            (ref MessagePackWriter writer) => MessagePackSerializer.Serialize(ref writer, surface, RemoteLink.Options));

    public void ShowFrame(ulong frame) =>
        link.Tell((int)ViewerMethod.ShowFrame, (ref MessagePackWriter writer) => writer.Write(frame));

    public void EndUniverse() => link.Tell((int)ViewerMethod.EndUniverse, null);
}
