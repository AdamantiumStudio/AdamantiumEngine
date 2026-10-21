using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Adamantium.Core;

namespace Adamantium.DBus;

/// <summary>A connection to a message bus. Replies and signals are read on a thread of its own, and a signal's handler
/// runs there: it may await a call, but not wait for one.</summary>
public sealed class DBusConnection : IDisposable
{
    private const string BusName = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";
    private const string ClosedError = "Adamantium.DBus.Closed";

    private readonly Socket socket;
    private readonly NetworkStream stream;
    private readonly Thread readerThread;
    private readonly object _writeLock = new();
    private readonly object _pendingLock = new();
    private readonly object _subscriptionsLock = new();
    private readonly Dictionary<uint, TaskCompletionSource<DBusMessage>> pending = [];
    private readonly List<Subscription> subscriptions = [];
    private uint lastSerial;
    private int disposed;
    private volatile bool closed;

    private DBusConnection(Socket socket)
    {
        this.socket = socket;
        stream = new NetworkStream(socket, true);
        Authenticate();
        readerThread = new Thread(ReadMessages) { IsBackground = true, Name = "D-Bus reader" };
        readerThread.Start();
        UniqueName = Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "Hello")).Reader().ReadString();
    }

    /// <summary>The name the bus gave this connection, as :1.42.</summary>
    public string UniqueName { get; }

    /// <summary>The user's session bus: DBUS_SESSION_BUS_ADDRESS, else the bus in XDG_RUNTIME_DIR.</summary>
    public static DBusConnection ConnectSession()
    {
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address) && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime)
        {
            address = "unix:path=" + Path.Combine(runtime, "bus");
        }

        return Connect(address ?? throw new DBusException("Adamantium.DBus.NoAddress", "no session bus address"));
    }

    /// <summary>A bus at a D-Bus address: the first of its unix:path= and unix:abstract= entries that answers.</summary>
    public static DBusConnection Connect(string address)
    {
        Exception failure = null;
        foreach (var entry in address.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (SocketPath(entry) is not { } path)
            {
                continue;
            }

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(path));
                return new DBusConnection(socket);
            }
            catch (Exception e) when (e is SocketException or IOException or DBusException)
            {
                socket.Dispose();
                failure = e;
            }
        }

        throw failure as DBusException ?? new DBusException("Adamantium.DBus.BadAddress",
            failure == null ? $"no unix socket in '{address}'" : failure.Message);
    }

    /// <summary>Sends a call and gives its reply; an error reply is thrown as a <see cref="DBusException"/>.</summary>
    public async Task<DBusMessage> CallAsync(DBusMessage call, CancellationToken cancellation = default)
    {
        var reply = new TaskCompletionSource<DBusMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        uint serial;
        lock (_writeLock)
        {
            ThrowIfClosed();
            serial = ++lastSerial;
            lock (_pendingLock)
            {
                pending[serial] = reply;
            }

            Write(call, serial);
        }

        await using var registration = cancellation.Register(() =>
        {
            Forget(serial);
            reply.TrySetCanceled(cancellation);
        });
        var message = await reply.Task.ConfigureAwait(false);
        if (message.Type == DBusMessageType.Error)
        {
            var text = message.Signature.StartsWith('s') ? message.Reader().ReadString() : string.Empty;
            throw new DBusException(message.ErrorName, text);
        }

        return message;
    }

    /// <summary>Waits for a call's reply; never from a signal handler, which runs on the thread replies come on.</summary>
    public DBusMessage Call(DBusMessage call)
    {
        if (Thread.CurrentThread == readerThread)
        {
            throw new InvalidOperationException("A signal handler waited for a reply its own thread has to read: await CallAsync there.");
        }

        return CallAsync(call).GetAwaiter().GetResult();
    }

    /// <summary>Sends a message no reply is waited for.</summary>
    public void Send(DBusMessage message)
    {
        lock (_writeLock)
        {
            ThrowIfClosed();
            Write(message, ++lastSerial);
        }
    }

    /// <summary>Raises <paramref name="handler"/> for every signal that matches, until the result is disposed.</summary>
    public IDisposable Subscribe(string sender, string path, string interfaceName, string member, Action<DBusMessage> handler)
    {
        var owner = sender == null || sender.StartsWith(':') ? sender : OwnerOf(sender) ?? sender;
        var rule = MatchRule(sender, path, interfaceName, member);
        var subscription = new Subscription(this, rule, owner, path, interfaceName, member, handler);
        lock (_subscriptionsLock)
        {
            subscriptions.Add(subscription);
        }

        try
        {
            Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "AddMatch", "s", writer => writer.WriteString(rule)));
        }
        catch
        {
            lock (_subscriptionsLock)
            {
                subscriptions.Remove(subscription);
            }

            throw;
        }

        return subscription;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        closed = true;
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }

        stream.Dispose();
        Fail();
    }

    private static string SocketPath(string entry)
    {
        if (!entry.StartsWith("unix:", StringComparison.Ordinal))
        {
            return null;
        }

        var keys = entry[5..].Split(',').Select(pair => pair.Split('=', 2)).Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
        return keys.TryGetValue("path", out var file) ? file
            : keys.TryGetValue("abstract", out var name) ? "\0" + name
            : null;
    }

    private string OwnerOf(string name)
    {
        try
        {
            return Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "GetNameOwner", "s", writer => writer.WriteString(name)))
                .Reader().ReadString();
        }
        catch (DBusException)
        {
            return null;
        }
    }

    private static string MatchRule(string sender, string path, string interfaceName, string member)
    {
        var rule = new StringBuilder("type='signal'");
        Append(rule, "sender", sender);
        Append(rule, "path", path);
        Append(rule, "interface", interfaceName);
        Append(rule, "member", member);
        return rule.ToString();
    }

    private static void Append(StringBuilder rule, string key, string value)
    {
        if (value != null)
        {
            rule.Append(',').Append(key).Append("='").Append(value.Replace("'", "'\\''")).Append('\'');
        }
    }

    private void Authenticate()
    {
        var uid = PosixInterop.GetUid().ToString(CultureInfo.InvariantCulture);
        var hex = Convert.ToHexString(Encoding.ASCII.GetBytes(uid)).ToLowerInvariant();
        stream.WriteByte(0);
        WriteLine($"AUTH EXTERNAL {hex}");
        var answer = ReadLine();
        if (!answer.StartsWith("OK ", StringComparison.Ordinal))
        {
            throw new DBusException("Adamantium.DBus.AuthFailed", answer);
        }

        WriteLine("BEGIN");
    }

    private void WriteLine(string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        stream.Write(bytes);
    }

    private string ReadLine()
    {
        var line = new StringBuilder();
        while (true)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                throw new DBusException(ClosedError, "the bus closed the connection while authenticating");
            }

            if (next == '\n' && line.Length > 0 && line[^1] == '\r')
            {
                return line.ToString(0, line.Length - 1);
            }

            line.Append((char)next);
        }
    }

    private void ThrowIfClosed()
    {
        if (closed)
        {
            throw new DBusException(ClosedError, "the connection to the bus is closed");
        }
    }

    private void Write(DBusMessage message, uint serial)
    {
        try
        {
            stream.Write(message.Serialize(serial));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            Forget(serial);
            closed = true;
            throw new DBusException(ClosedError, e.Message);
        }
    }

    private void Forget(uint serial)
    {
        lock (_pendingLock)
        {
            pending.Remove(serial);
        }
    }

    private void ReadMessages()
    {
        try
        {
            var header = new byte[DBusMessage.FixedHeaderLength];
            while (!closed)
            {
                stream.ReadExactly(header);
                if (header[0] != DBusMessage.LittleEndian)
                {
                    break;
                }

                var message = new byte[DBusMessage.LengthOf(header)];
                header.CopyTo(message, 0);
                stream.ReadExactly(message, header.Length, message.Length - header.Length);
                Dispatch(DBusMessage.Parse(message));
            }
        }
        catch (Exception)
        {
        }

        closed = true;
        Fail();
    }

    private void Dispatch(DBusMessage message)
    {
        switch (message.Type)
        {
            case DBusMessageType.MethodReturn or DBusMessageType.Error:
            {
                TaskCompletionSource<DBusMessage> reply;
                lock (_pendingLock)
                {
                    pending.Remove(message.ReplySerial, out reply);
                }

                reply?.TrySetResult(message);
                break;
            }
            case DBusMessageType.MethodCall when !message.NoReplyExpected:
                Send(DBusMessage.Error(message, "org.freedesktop.DBus.Error.UnknownMethod",
                    $"{message.Interface}.{message.Member} is not served here"));
                break;
            case DBusMessageType.Signal:
                Raise(message);
                break;
        }
    }

    private void Raise(DBusMessage signal)
    {
        Subscription[] matching;
        lock (_subscriptionsLock)
        {
            matching = subscriptions.Where(subscription => subscription.Matches(signal)).ToArray();
        }

        foreach (var subscription in matching)
        {
            try
            {
                subscription.Handler(signal);
            }
            catch (Exception)
            {
            }
        }
    }

    private void Fail()
    {
        TaskCompletionSource<DBusMessage>[] waiting;
        lock (_pendingLock)
        {
            waiting = pending.Values.ToArray();
            pending.Clear();
        }

        foreach (var reply in waiting)
        {
            reply.TrySetException(new DBusException(ClosedError, "the connection to the bus is closed"));
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_subscriptionsLock)
        {
            subscriptions.Remove(subscription);
        }

        if (!closed)
        {
            _ = CallAsync(DBusMessage.MethodCall(BusName, BusPath, BusName, "RemoveMatch", "s",
                writer => writer.WriteString(subscription.Rule))).ContinueWith(task => task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private sealed class Subscription(
        DBusConnection connection, string rule, string owner, string path, string interfaceName, string member,
        Action<DBusMessage> handler) : IDisposable
    {
        private int disposed;

        public string Rule { get; } = rule;

        public Action<DBusMessage> Handler { get; } = handler;

        public bool Matches(DBusMessage message) =>
            (owner == null || owner == message.Sender) &&
            (path == null || path == message.Path) &&
            (interfaceName == null || interfaceName == message.Interface) &&
            (member == null || member == message.Member);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                connection.Unsubscribe(this);
            }
        }
    }
}
