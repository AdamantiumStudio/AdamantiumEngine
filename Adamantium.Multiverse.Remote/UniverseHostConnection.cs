using System.IO.Pipes;

namespace Adamantium.Multiverse.Remote;

/// <summary>The viewer's connection to a universe host: <see cref="Host"/> calls the host's service, the viewer given
/// is told what the host tells it - on the connection's own reading thread, in order.</summary>
public sealed class UniverseHostConnection : IDisposable
{
    private readonly RemoteLink _link;

    private UniverseHostConnection(RemoteLink link)
    {
        _link = link;
        Host = new UniverseHostServiceProxy(link);
    }

    /// <summary>The host's service.</summary>
    public IUniverseHostService Host { get; }

    /// <summary>Done once the connection is closed, from either end. The host does not close it: the viewer disposes it
    /// once the universe ended or the launch failed.</summary>
    public Task Completion => _link.Completion;

    /// <summary>Connects to the host serving on <paramref name="pipeName"/>, for <paramref name="viewer"/> to be told
    /// what it shows.</summary>
    public static async Task<UniverseHostConnection> ConnectAsync(string pipeName, IUniverseViewer viewer,
        CancellationToken cancellationToken)
    {
        var toHost = Open(UniversePipes.ToHost(pipeName));
        var toViewer = Open(UniversePipes.ToViewer(pipeName));
        try
        {
            await toHost.ConnectAsync(cancellationToken);
            await toViewer.ConnectAsync(cancellationToken);
            var link = new RemoteLink(toViewer, toHost, new UniverseViewerDispatch(viewer), "Universe host link");
            var connection = new UniverseHostConnection(link);
            link.Start();
            return connection;
        }
        catch
        {
            await toHost.DisposeAsync();
            await toViewer.DisposeAsync();
            throw;
        }
    }

    public void Dispose() => _link.Dispose();

    private static NamedPipeClientStream Open(string name) =>
        new(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
}
