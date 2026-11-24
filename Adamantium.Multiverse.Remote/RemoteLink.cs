using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using MessagePack;
using MessagePack.Resolvers;
using Serilog;

namespace Adamantium.Multiverse.Remote;

/// <summary>Messages between a universe host and its viewer, each way on a pipe of its own, read and written on threads
/// of the link's own - never the thread pool's: a host held by a debugger, its pool held with it, still hears its viewer
/// and answers it on the threads the debugger lets run. A message is its length, then MessagePack: its kind, its call's
/// id, its method, and what it carries.</summary>
internal sealed class RemoteLink : IDisposable
{
    /// <summary>How the engine's types are written: by their public members.</summary>
    public static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private readonly Stream _incoming;
    private readonly Stream _outgoing;
    private readonly IRemoteHandler _handler;
    private readonly BlockingCollection<byte[]> _sending = new();
    private readonly ConcurrentDictionary<int, PendingCall> _calls = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _reading;
    private readonly Thread _writing;
    private int _lastId;
    private int _ended;

    /// <summary>The link over <paramref name="incoming"/> and <paramref name="outgoing"/>, which it owns, serving
    /// <paramref name="handler"/>; its threads named after <paramref name="threadName"/>. Not started yet.</summary>
    public RemoteLink(Stream incoming, Stream outgoing, IRemoteHandler handler, string threadName)
    {
        _incoming = incoming;
        _outgoing = outgoing;
        _handler = handler;
        _reading = new Thread(Read) { IsBackground = true, Name = threadName + ": reading" };
        _writing = new Thread(Write) { IsBackground = true, Name = threadName + ": writing" };
    }

    /// <summary>What a message carries, written in order.</summary>
    public delegate void ArgumentWriter(ref MessagePackWriter writer);

    /// <summary>Done once the link closed, from either end.</summary>
    public Task Completion => _completion.Task;

    public void Start()
    {
        _reading.Start();
        _writing.Start();
    }

    /// <summary>Calls <paramref name="method"/> at the other end, not waiting for it.</summary>
    public void Tell(int method, ArgumentWriter arguments) => Send(RemoteMessageKind.Notice, 0, method, arguments);

    /// <summary>Calls <paramref name="method"/> at the other end: done with its answer, failed with its reason
    /// (<see cref="UniverseHostException"/>), or called off - at once, the other end told. A link closed fails it.</summary>
    public Task<T> CallAsync<T>(int method, ArgumentWriter arguments, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var id = Interlocked.Increment(ref _lastId);
        var call = new PendingCall<T>();
        _calls[id] = call;
        if (Volatile.Read(ref _ended) != 0 && _calls.TryRemove(id, out _))
        {
            call.Lose();
            return call.Task;
        }

        // Watched before it is sent: an answer that comes at once finds the watch to end.
        call.Watch(cancellationToken, () =>
        {
            if (_calls.TryRemove(id, out _))
            {
                Send(RemoteMessageKind.CallOff, id, 0, null);
                call.CallOff(cancellationToken);
            }
        });
        Send(RemoteMessageKind.Call, id, method, arguments);
        return call.Task;
    }

    /// <summary>Answers call <paramref name="id"/>: done, with nothing.</summary>
    public void Reply(int id) => Send(RemoteMessageKind.Result, id, 0, static (ref MessagePackWriter writer) => writer.WriteNil());

    /// <summary>Answers call <paramref name="id"/> with <paramref name="value"/>.</summary>
    public void Reply<T>(int id, T value) =>
        Send(RemoteMessageKind.Result, id, 0, (ref MessagePackWriter writer) => MessagePackSerializer.Serialize(ref writer, value, Options));

    /// <summary>Answers call <paramref name="id"/>: failed, for <paramref name="reason"/>.</summary>
    public void Fail(int id, string reason) =>
        Send(RemoteMessageKind.Failure, id, 0, (ref MessagePackWriter writer) => writer.Write(reason));

    /// <summary>Answers call <paramref name="id"/>: called off.</summary>
    public void Cancel(int id) => Send(RemoteMessageKind.Canceled, id, 0, null);

    public void Dispose() => End();

    private void Send(RemoteMessageKind kind, int id, int method, ArgumentWriter arguments)
    {
        var payload = new ArrayBufferWriter<byte>(64);
        var writer = new MessagePackWriter(payload);
        writer.Write((byte)kind);
        writer.Write(id);
        writer.Write(method);
        arguments?.Invoke(ref writer);
        writer.Flush();

        var frame = new byte[sizeof(int) + payload.WrittenCount];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.WrittenCount);
        payload.WrittenSpan.CopyTo(frame.AsSpan(sizeof(int)));
        try
        {
            _sending.Add(frame);
        }
        catch (InvalidOperationException)
        {
            // Closed: nothing reaches the other end any more.
        }
    }

    private void Read()
    {
        var header = new byte[sizeof(int)];
        try
        {
            while (ReadFully(header))
            {
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 0 or > LongestMessage)
                {
                    throw new IOException($"A message of {length} bytes on the universe link is no message.");
                }

                var payload = new byte[length];
                if (!ReadFully(payload))
                {
                    break;
                }

                Take(payload);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The other end went, or sends what is no message: the link ends.
        }
        catch (Exception exception) when (exception is MessagePackSerializationException or InvalidOperationException)
        {
            Log.Logger.Error(exception, "Universe link: a message could not be read; the link ends");
        }

        End();
    }

    // The largest a message is let be: a frame read back as a PNG, of the largest screen, is well under it.
    private const int LongestMessage = 512 * 1024 * 1024;

    // False at the pipe's end before the first byte; a message cut off throws.
    private bool ReadFully(byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = _incoming.Read(buffer, read, buffer.Length - read);
            if (count == 0)
            {
                return read == 0 ? false : throw new EndOfStreamException("A message on the universe link was cut off.");
            }

            read += count;
        }

        return true;
    }

    private void Take(byte[] payload)
    {
        var reader = new MessagePackReader(payload);
        var kind = (RemoteMessageKind)reader.ReadByte();
        var id = reader.ReadInt32();
        var method = reader.ReadInt32();
        switch (kind)
        {
            // An answer that cannot be read fails its call, not the link.
            case RemoteMessageKind.Result when _calls.TryRemove(id, out var answered):
                try
                {
                    answered.Complete(ref reader);
                }
                catch (MessagePackSerializationException unreadable)
                {
                    answered.Break(unreadable);
                }

                return;
            case RemoteMessageKind.Failure when _calls.TryRemove(id, out var failed):
                failed.Fail(reader.ReadString());
                return;
            case RemoteMessageKind.Canceled when _calls.TryRemove(id, out var canceled):
                canceled.Cancel();
                return;
        }

        // What the other end asks: a mistake in serving it is told, and the link goes on with the next.
        try
        {
            switch (kind)
            {
                case RemoteMessageKind.Notice:
                    _handler.Hear(method, ref reader);
                    break;
                case RemoteMessageKind.Call:
                    _handler.Answer(method, id, ref reader);
                    break;
                case RemoteMessageKind.CallOff:
                    _handler.CallOff(id);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Logger.Error(exception, "Universe link: message {Kind} {Method} not served", kind, method);
            if (kind == RemoteMessageKind.Call)
            {
                Fail(id, exception.Message);
            }
        }
    }

    private void Write()
    {
        try
        {
            foreach (var frame in _sending.GetConsumingEnumerable())
            {
                _outgoing.Write(frame);
                _outgoing.Flush();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The other end went: the link ends.
        }

        End();
    }

    // Once, from whichever thread sees it first: what is waited for is lost, and the pipes are let go of. That ends the
    // writing; a reading blocked in the pipe ends only as the other end lets go of its own - its thread is a background
    // one, and the other end's link ends with this one.
    private void End()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            return;
        }

        _sending.CompleteAdding();
        foreach (var id in _calls.Keys)
        {
            if (_calls.TryRemove(id, out var call))
            {
                call.Lose();
            }
        }

        _incoming.Dispose();
        _outgoing.Dispose();
        _completion.TrySetResult();
    }

    private abstract class PendingCall
    {
        public abstract void Complete(ref MessagePackReader reader);

        public abstract void Fail(string reason);

        public abstract void Cancel();

        public abstract void Lose();

        /// <summary>Fails the call with what went wrong reading its answer.</summary>
        public abstract void Break(Exception exception);
    }

    private sealed class PendingCall<T> : PendingCall
    {
        private readonly TaskCompletionSource<T> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _callOff;

        public Task<T> Task => _done.Task;

        public void Watch(CancellationToken cancellationToken, Action callOff)
        {
            if (cancellationToken.CanBeCanceled)
            {
                _callOff = cancellationToken.Register(callOff);
            }
        }

        public void CallOff(CancellationToken cancellationToken) => _done.TrySetCanceled(cancellationToken);

        public override void Complete(ref MessagePackReader reader)
        {
            _callOff.Unregister();
            _done.TrySetResult(reader.TryReadNil() ? default : MessagePackSerializer.Deserialize<T>(ref reader, Options));
        }

        public override void Fail(string reason)
        {
            _callOff.Unregister();
            _done.TrySetException(new UniverseHostException(reason));
        }

        public override void Cancel()
        {
            _callOff.Unregister();
            _done.TrySetCanceled();
        }

        public override void Lose()
        {
            _callOff.Unregister();
            _done.TrySetException(new IOException("The universe link closed before the call was answered."));
        }

        public override void Break(Exception exception)
        {
            _callOff.Unregister();
            _done.TrySetException(exception);
        }
    }
}
