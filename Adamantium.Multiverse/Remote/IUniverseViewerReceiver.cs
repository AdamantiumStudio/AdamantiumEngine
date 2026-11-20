namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe's viewer is told by its host, on the channel's reading thread.</summary>
public interface IUniverseViewerReceiver
{
    void Greet(RemoteHello hello);

    /// <summary>Takes the frames of <paramref name="surface"/> from now on; it already holds one.</summary>
    void ShowSurface(RemoteSurface surface);

    /// <summary>Frame <paramref name="frame"/> was submitted; the surface's produce timeline says when it is drawn.
    /// </summary>
    void ShowFrame(ulong frame);

    /// <summary>The universe stopped: no frame comes any more.</summary>
    void EndUniverse();

    /// <summary>The host is gone: the channel is closed.</summary>
    void LoseHost();
}
