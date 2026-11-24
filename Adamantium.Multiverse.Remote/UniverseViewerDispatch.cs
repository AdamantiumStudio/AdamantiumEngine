using MessagePack;

namespace Adamantium.Multiverse.Remote;

/// <summary>The viewer's end of the link: what its host tells, given to the viewer on the link's reading thread, in
/// order.</summary>
internal sealed class UniverseViewerDispatch(IUniverseViewer viewer) : IRemoteHandler
{
    public void Hear(int method, ref MessagePackReader arguments)
    {
        switch ((ViewerMethod)method)
        {
            case ViewerMethod.Greet:
                viewer.Greet(MessagePackSerializer.Deserialize<RemoteHello>(ref arguments, RemoteLink.Options));
                break;
            case ViewerMethod.ShowSurface:
                viewer.ShowSurface(MessagePackSerializer.Deserialize<RemoteSurface>(ref arguments, RemoteLink.Options));
                break;
            case ViewerMethod.ShowFrame:
                viewer.ShowFrame(arguments.ReadUInt64());
                break;
            case ViewerMethod.EndUniverse:
                viewer.EndUniverse();
                break;
            default:
                throw new InvalidOperationException($"{(ViewerMethod)method} is not a notice of the viewer.");
        }
    }

    public void Answer(int method, int id, ref MessagePackReader arguments) =>
        throw new InvalidOperationException("The viewer answers no calls.");

    public void CallOff(int id)
    {
    }
}
