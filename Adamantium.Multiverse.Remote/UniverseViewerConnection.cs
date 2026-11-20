using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Win32;
using StreamJsonRpc;

namespace Adamantium.Multiverse.Remote;

/// <summary>A universe host's connection to its viewer: the host serves <see cref="IUniverseHostService"/> on a pipe of
/// its own, the viewer connects, and <see cref="Viewer"/> tells it what to show.</summary>
public sealed class UniverseViewerConnection : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly JsonRpc _rpc;

    private UniverseViewerConnection(NamedPipeServerStream pipe, JsonRpc rpc, int viewerProcessId)
    {
        _pipe = pipe;
        _rpc = rpc;
        ViewerProcessId = viewerProcessId;
        Viewer = rpc.Attach<IUniverseViewer>();
    }

    /// <summary>The viewer, told what the host shows.</summary>
    public IUniverseViewer Viewer { get; }

    /// <summary>The viewer's process, which handles shared with it are given to.</summary>
    public int ViewerProcessId { get; }

    /// <summary>Done once the connection is closed, from either end.</summary>
    public Task Completion => _rpc.Completion;

    /// <summary>Serves <paramref name="service"/> on <paramref name="pipeName"/> to the first viewer that connects.</summary>
    public static async Task<UniverseViewerConnection> AcceptAsync(string pipeName, IUniverseHostService service,
        CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            var viewerProcessId = ClientProcessId(pipe);
            var rpc = UniverseRpc.Create(pipe, service);
            var connection = new UniverseViewerConnection(pipe, rpc, viewerProcessId);
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

    private static unsafe int ClientProcessId(NamedPipeServerStream pipe)
    {
        var handle = pipe.SafePipeHandle.DangerousGetHandle();
        if (OperatingSystem.IsWindows())
        {
            if (!Win32Interop.GetNamedPipeClientProcessId(handle, out var processId))
            {
                throw new Win32Exception();
            }

            return (int)processId;
        }

        if (OperatingSystem.IsMacOS())
        {
            var peer = 0;
            var size = (uint)sizeof(int);
            if (PosixInterop.GetSocketOption((int)handle, PosixInterop.LocalSocketLevel, PosixInterop.LocalPeerProcessId,
                    &peer, ref size) != 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return peer;
        }

        var credentials = stackalloc int[3];
        var length = (uint)(3 * sizeof(int));
        if (PosixInterop.GetSocketOption((int)handle, PosixInterop.SocketLevel, PosixInterop.SocketPeerCredentials,
                credentials, ref length) != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return credentials[0];
    }
}
