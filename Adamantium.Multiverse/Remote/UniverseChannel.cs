using System.Buffers.Binary;
using System.Text;
using Serilog;

namespace Adamantium.Multiverse.Remote;

/// <summary>One end of the channel between a universe host and its viewer, over a stream both read and write - a pipe:
/// messages of a kind and a length, written from any thread, read on a thread of the channel's own and handed to the
/// receiver there.</summary>
public abstract class UniverseChannel : IDisposable
{
    private const int HeaderSize = 5;
    private const int MaxMessageSize = 1 << 20;

    private readonly Stream _stream;
    private readonly object _writeGate = new();
    private readonly MemoryStream _writeBuffer = new();
    private readonly BinaryWriter _writer;
    private readonly byte[] _header = new byte[HeaderSize];
    private byte[] _readBuffer = new byte[256];
    private MemoryStream _readStream;
    private BinaryReader _reader;
    private Thread _readingThread;
    private volatile bool _disposed;

    protected UniverseChannel(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _writer = new BinaryWriter(_writeBuffer, Encoding.UTF8, leaveOpen: true);
        _readStream = new MemoryStream(_readBuffer, writable: true);
        _reader = new BinaryReader(_readStream, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>Starts reading the other end's messages, on a thread of the channel's own.</summary>
    public void Start()
    {
        if (_readingThread != null)
        {
            return;
        }

        _readingThread = new Thread(Read) { IsBackground = true, Name = GetType().Name };
        _readingThread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_writeGate)
        {
            _disposed = true;
            _stream.Dispose();
        }

        if (_readingThread != null && _readingThread != Thread.CurrentThread)
        {
            _readingThread.Join();
        }
    }

    /// <summary>Held while a message is written, from <see cref="StartMessage"/> to <see cref="FinishMessage"/>.</summary>
    protected object WriteGate => _writeGate;

    /// <summary>The writer of a new message's payload; under <see cref="WriteGate"/>.</summary>
    protected BinaryWriter StartMessage()
    {
        _writeBuffer.SetLength(HeaderSize);
        _writeBuffer.Position = HeaderSize;
        return _writer;
    }

    /// <summary>Sends the message started by <see cref="StartMessage"/> as <paramref name="kind"/>; under
    /// <see cref="WriteGate"/>. A closed channel sends nothing.</summary>
    protected void FinishMessage(UniverseMessageKind kind)
    {
        if (_disposed)
        {
            return;
        }

        _writer.Flush();
        var message = _writeBuffer.GetBuffer();
        message[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(1), (int)_writeBuffer.Length - HeaderSize);
        try
        {
            _stream.Write(message, 0, (int)_writeBuffer.Length);
            _stream.Flush();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Sends a message with no payload.</summary>
    protected void SendEmpty(UniverseMessageKind kind)
    {
        lock (_writeGate)
        {
            StartMessage();
            FinishMessage(kind);
        }
    }

    protected abstract void Receive(UniverseMessageKind kind, BinaryReader payload);

    protected abstract void LoseOtherEnd();

    private void Read()
    {
        try
        {
            while (!_disposed && ReadExactly(_header, HeaderSize))
            {
                var kind = (UniverseMessageKind)_header[0];
                var length = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(1));
                if (length < 0 || length > MaxMessageSize)
                {
                    break;
                }

                MakeRoomFor(length);
                _readStream.SetLength(length);
                if (!ReadExactly(_readBuffer, length))
                {
                    break;
                }

                _readStream.Position = 0;
                Receive(kind, _reader);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "A universe channel's message could not be taken; the channel is closed");
        }
        finally
        {
            if (!_disposed)
            {
                LoseOtherEnd();
            }
        }
    }

    private bool ReadExactly(byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var got = _stream.Read(buffer, read, count - read);
            if (got == 0)
            {
                return false;
            }

            read += got;
        }

        return true;
    }

    private void MakeRoomFor(int length)
    {
        if (length <= _readBuffer.Length)
        {
            return;
        }

        _readBuffer = new byte[Math.Max(length, _readBuffer.Length * 2)];
        _readStream = new MemoryStream(_readBuffer, writable: true);
        _reader = new BinaryReader(_readStream, Encoding.UTF8, leaveOpen: true);
    }
}
