using System.Collections.Concurrent;
using Adamantium.Multiverse.Input;
using MessagePack;

namespace Adamantium.Multiverse.Remote;

/// <summary>The host's end of the link: what its viewer calls, given to the host's service. Notices are served on the
/// link's reading thread as they come, in order; a call is answered as its task ends, and called off by its token.</summary>
internal sealed class UniverseHostServiceDispatch(IUniverseHostService service) : IRemoteHandler
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();

    /// <summary>The link the answers go back on; set before it starts.</summary>
    public RemoteLink Link { get; set; }

    public void Hear(int method, ref MessagePackReader arguments)
    {
        switch ((HostMethod)method)
        {
            case HostMethod.Resize:
                service.Resize(arguments.ReadUInt32(), arguments.ReadUInt32(), arguments.ReadSingle());
                break;
            case HostMethod.AskFrame:
                service.AskFrame(arguments.ReadUInt64());
                break;
            case HostMethod.AskNewSurface:
                service.AskNewSurface();
                break;
            case HostMethod.DrawWhereItStands:
                service.DrawWhereItStands();
                break;
            case HostMethod.MovePointer:
                service.MovePointer(arguments.ReadSingle(), arguments.ReadSingle(), arguments.ReadBoolean());
                break;
            case HostMethod.Focus:
                service.Focus(arguments.ReadBoolean());
                break;
            case HostMethod.TakeKeyboardInput:
                service.TakeKeyboardInput(Read<KeyboardInput>(ref arguments));
                break;
            case HostMethod.TakeMouseInput:
                service.TakeMouseInput(Read<MouseInput>(ref arguments));
                break;
            case HostMethod.TakeGestureInput:
                service.TakeGestureInput(Read<GestureInput>(ref arguments));
                break;
            case HostMethod.TakeText:
                service.TakeText(arguments.ReadString());
                break;
            default:
                throw new InvalidOperationException($"{(HostMethod)method} is not a notice of the universe host.");
        }
    }

    public void Answer(int method, int id, ref MessagePackReader arguments)
    {
        var calledOff = new CancellationTokenSource();
        _running[id] = calledOff;
        var token = calledOff.Token;
        try
        {
            switch ((HostMethod)method)
            {
                case HostMethod.Launch:
                    Reply(id, service.LaunchAsync(Read<UniverseLaunch>(ref arguments), token));
                    break;
                case HostMethod.Stop:
                    Reply(id, service.StopAsync(token));
                    break;
                case HostMethod.GoOn:
                    Reply(id, service.GoOnAsync(token));
                    break;
                case HostMethod.CaptureScene:
                    Reply(id, service.CaptureSceneAsync(token));
                    break;
                case HostMethod.CaptureFrame:
                    Reply(id, service.CaptureFrameAsync(token));
                    break;
                default:
                    throw new InvalidOperationException($"{(HostMethod)method} is not a call of the universe host.");
            }
        }
        catch
        {
            // Failed before it ran: the link answers it with why.
            Forget(id);
            throw;
        }
    }

    public void CallOff(int id)
    {
        if (!_running.TryGetValue(id, out var calledOff))
        {
            return;
        }

        try
        {
            calledOff.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Done meanwhile, and let go of: nothing is left to call off.
        }
    }

    private async void Reply(int id, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            Link.Reply(id);
        }
        catch (Exception exception)
        {
            Fail(id, exception);
        }
        finally
        {
            Forget(id);
        }
    }

    private async void Reply<T>(int id, Task<T> task)
    {
        try
        {
            Link.Reply(id, await task.ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            Fail(id, exception);
        }
        finally
        {
            Forget(id);
        }
    }

    private void Fail(int id, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            Link.Cancel(id);
        }
        else
        {
            Link.Fail(id, exception.Message);
        }
    }

    private void Forget(int id)
    {
        if (_running.TryRemove(id, out var calledOff))
        {
            calledOff.Dispose();
        }
    }

    private static T Read<T>(ref MessagePackReader reader) => MessagePackSerializer.Deserialize<T>(ref reader, RemoteLink.Options);
}
