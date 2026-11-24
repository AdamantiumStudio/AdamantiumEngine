namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host tells its viewer, not waiting for it.</summary>
public interface IUniverseViewer
{
    /// <summary>Told first, before any surface: the host's process and the GPU it draws on, which the viewer's must be to
    /// take its surfaces.</summary>
    void Greet(RemoteHello hello);

    /// <summary>Takes the frames of <paramref name="surface"/> from now on; it already holds one.</summary>
    void ShowSurface(RemoteSurface surface);

    /// <summary>Frame <paramref name="frame"/> was submitted; the surface says when it is drawn.</summary>
    void ShowFrame(ulong frame);

    /// <summary>The universe stopped: no frame comes any more.</summary>
    void EndUniverse();
}
