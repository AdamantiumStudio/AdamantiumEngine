using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Adamantium.Pulsar.Tests;

/// <summary>A station on this computer, as Icecast sends one: over HTTP, its MP3 file again and again, with ICY's
/// titles every <see cref="Interval"/> bytes where asked for them. A connection may be dropped after so many bytes; the
/// station may send AAC, or stop answering. No network beyond this computer.</summary>
internal sealed class StationServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly byte[] _sound;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<TcpClient> _clients = [];
    private int _connections;

    public StationServer(byte[] sound)
    {
        _sound = sound;
        _listener.Start();
        Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/stream");
        _ = AcceptAsync();
    }

    public Uri Address { get; }

    public int Interval { get; init; } = 8000;

    public string Name { get; init; } = "Test Radio";

    /// <summary>The titles sent in turn, one each text; the last again after them.</summary>
    public string[] Titles { get; init; } = ["Artist - First Song"];

    public string ContentType { get; init; } = "audio/mpeg";

    /// <summary>Of each of the first <see cref="Drops"/> connections, the bytes sent before it is closed.</summary>
    public long DropAfter { get; init; } = long.MaxValue;

    public int Drops { get; init; }

    /// <summary>The connections after these are refused - closed at once - as a station gone away.</summary>
    public int Answers { get; set; } = int.MaxValue;

    public int Connections => Volatile.Read(ref _connections);

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception)
            {
                return;
            }

            lock (_clients)
            {
                _clients.Add(client);
            }

            _ = SendAsync(client, Interlocked.Increment(ref _connections));
        }
    }

    private async Task SendAsync(TcpClient client, int connection)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (connection > Answers)
                {
                    return;
                }

                var withTitles = request.Contains("icy-metadata: 1", StringComparison.OrdinalIgnoreCase);
                var headers = $"HTTP/1.1 200 OK\r\nContent-Type: {ContentType}\r\nicy-name: {Name}\r\nicy-br: 128\r\n" +
                    (withTitles ? $"icy-metaint: {Interval}\r\n" : "") + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _stopping.Token);
                var limit = connection <= Drops ? DropAfter : long.MaxValue;
                await SendSoundAsync(stream, withTitles, limit);
            }
            catch (Exception)
            {
                // The player went away, or the station was stopped.
            }
        }
    }

    // The file again and again, a title's text after every interval's bytes, until the limit or the end of the test.
    private async Task SendSoundAsync(NetworkStream stream, bool withTitles, long limit)
    {
        long sent = 0;
        var untilText = Interval;
        var title = 0;
        var at = 0;
        while (sent < limit && !_stopping.IsCancellationRequested)
        {
            var run = (int)Math.Min(Math.Min(_sound.Length - at, withTitles ? untilText : int.MaxValue), limit - sent);
            await stream.WriteAsync(_sound.AsMemory(at, run), _stopping.Token);
            sent += run;
            at = (at + run) % _sound.Length;
            untilText -= run;
            if (withTitles && untilText == 0)
            {
                var text = Encoding.UTF8.GetBytes($"StreamTitle='{Titles[Math.Min(title++, Titles.Length - 1)]}';");
                var blocks = (text.Length + 15) / 16;
                var block = new byte[1 + blocks * 16];
                block[0] = (byte)blocks;
                text.CopyTo(block, 1);
                await stream.WriteAsync(block, _stopping.Token);
                untilText = Interval;
            }
        }
    }

    private async Task<string> ReadRequestAsync(NetworkStream stream)
    {
        var request = new StringBuilder();
        var buffer = new byte[1024];
        while (!request.ToString().Contains("\r\n\r\n"))
        {
            var read = await stream.ReadAsync(buffer, _stopping.Token);
            if (read == 0)
            {
                break;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return request.ToString();
    }
}
