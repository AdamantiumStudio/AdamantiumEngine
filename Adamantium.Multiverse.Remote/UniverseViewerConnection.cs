using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Win32;

namespace Adamantium.Multiverse.Remote;

/// <summary>A universe host's connection to its viewer: the host serves <see cref="IUniverseHostService"/> on pipes of
/// its own, the viewer connects, and <see cref="Viewer"/> tells it what to show. The host hears and answers on threads
/// of the connection's own (<see cref="UniverseHostThreads.ViewerLink"/>), not the thread pool's - so a debugger that
/// holds the game can let them run.</summary>
public sealed class UniverseViewerConnection : IDisposable
{
    private readonly RemoteLink _link;

    private UniverseViewerConnection(RemoteLink link, int viewerProcessId)
    {
        _link = link;
        ViewerProcessId = viewerProcessId;
        Viewer = new UniverseViewerProxy(link);
    }

    /// <summary>The viewer, told what the host shows.</summary>
    public IUniverseViewer Viewer { get; }

    /// <summary>The viewer's process, which handles shared with it are given to.</summary>
    public int ViewerProcessId { get; }

    /// <summary>Done once the connection is closed, from either end.</summary>
    public Task Completion => _link.Completion;

    /// <summary>Serves <paramref name="service"/> on <paramref name="pipeName"/> to the first viewer that connects.</summary>
    public static async Task<UniverseViewerConnection> AcceptAsync(string pipeName, IUniverseHostService service,
        CancellationToken cancellationToken)
    {
        // One pipe each way, its reads and writes blocking on the link's own threads: one pipe both ways would have a
        // blocking read hold its writes back.
        var toHost = Serve(UniversePipes.ToHost(pipeName));
        NamedPipeServerStream toViewer = null;
        try
        {
            toViewer = Serve(UniversePipes.ToViewer(pipeName));
            await AcceptAsync(toHost, UniversePipes.ToHost(pipeName), cancellationToken);
            await AcceptAsync(toViewer, UniversePipes.ToViewer(pipeName), cancellationToken);
            var viewerProcessId = ClientProcessId(toHost);
            var dispatch = new UniverseHostServiceDispatch(service);
            var link = new RemoteLink(toHost, toViewer, dispatch, UniverseHostThreads.ViewerLink);
            dispatch.Link = link;
            var connection = new UniverseViewerConnection(link, viewerProcessId);
            link.Start();
            return connection;
        }
        catch
        {
            await toHost.DisposeAsync();
            if (toViewer != null)
            {
                await toViewer.DisposeAsync();
            }

            throw;
        }
    }

    public void Dispose() => _link.Dispose();

    private static NamedPipeServerStream Serve(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);

    // A blocking wait on a thread of its own; called off by a connection made to let it go - a pipe waited on is not
    // closed under the wait.
    private static async Task AcceptAsync(NamedPipeServerStream pipe, string name, CancellationToken cancellationToken)
    {
        await using (cancellationToken.Register(() => LetGo(name)))
        {
            await Task.Run(pipe.WaitForConnection, CancellationToken.None);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void LetGo(string name)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(1000);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // Connected already, or gone: the wait is over anyway.
        }
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
