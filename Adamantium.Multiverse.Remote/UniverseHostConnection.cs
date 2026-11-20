using System.IO.Pipes;
using StreamJsonRpc;

namespace Adamantium.Multiverse.Remote;

/// <summary>The viewer's connection to a universe host: <see cref="Host"/> calls the host's service, the viewer given
/// is told what the host tells it.</summary>
public sealed class UniverseHostConnection : IDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly JsonRpc _rpc;

    private UniverseHostConnection(NamedPipeClientStream pipe, JsonRpc rpc)
    {
        _pipe = pipe;
        _rpc = rpc;
        Host = rpc.Attach<IUniverseHostService>();
    }

    /// <summary>The host's service.</summary>
    public IUniverseHostService Host { get; }

    /// <summary>Done once the connection is closed, from either end. The host does not close it: the viewer disposes it
    /// once the universe ended or the launch failed.</summary>
    public Task Completion => _rpc.Completion;

    /// <summary>Connects to the host serving on <paramref name="pipeName"/>, for <paramref name="viewer"/> to be told
    /// what it shows.</summary>
    public static async Task<UniverseHostConnection> ConnectAsync(string pipeName, IUniverseViewer viewer,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(cancellationToken);
            var rpc = UniverseRpc.Create(pipe, viewer);
            var connection = new UniverseHostConnection(pipe, rpc);
            rpc.StartListening();
            return connection;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        _rpc.Dispose();
        _pipe.Dispose();
    }
}
