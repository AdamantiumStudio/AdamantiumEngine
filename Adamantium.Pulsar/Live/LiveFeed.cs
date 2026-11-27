namespace Adamantium.Pulsar.Live;

/// <summary>A station's sound, connected to, read and decoded on a thread of its own into samples a voice takes as it
/// plays: the engine's streamer never waits for the network. Dropped, it connects again - as many times as
/// <see cref="Waits"/> has waits, after each - while the samples it has play on; one that will not come back ends it,
/// with the reason.</summary>
internal sealed class LiveFeed : IDisposable
{
    /// <summary>A power of two: 5.5 s at 48 kHz, so a short drop is not heard.</summary>
    public const int Capacity = 1 << 18;

    private const int ChunkFrames = 4096;
    private static readonly TimeSpan[] DefaultWaits =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4)];
    private static readonly TimeSpan ReadyAfter = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RoomWait = TimeSpan.FromMilliseconds(20);

    private readonly Uri _address;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;
    private float[] _samples;
    private long _written;
    private long _read;
    private int _readyFrames;
    private bool _hasEnded;
    private string _problem;
    private int _leaving;

    /// <param name="waits">Before each connection again, after a drop; the default's are 1, 2 and 4 s, five in all.
    /// </param>
    public LiveFeed(Uri address, IReadOnlyList<TimeSpan> waits = null)
    {
        _address = address;
        Waits = waits ?? DefaultWaits;
        _thread = new Thread(Run) { Name = "Pulsar station", IsBackground = true };
    }

    /// <summary>Its sound's format is known - it connected, and its first frame was found: on the feed's thread.</summary>
    public event Action<LiveFeed> Began;

    /// <summary>The stream says another title: on the feed's thread.</summary>
    public event Action<LiveFeed, string> TitleChanged;

    /// <summary>It will not come back - <see cref="Problem"/> says why - and plays the samples it has to their end: on
    /// the feed's thread.</summary>
    public event Action<LiveFeed> Ended;

    public IReadOnlyList<TimeSpan> Waits { get; }

    /// <summary>Known once it <see cref="Began"/>.</summary>
    public AudioFormat Format { get; private set; }

    public string Codec { get; private set; }

    /// <summary>As the station says it; null where it does not.</summary>
    public string Station { get; private set; }

    /// <summary>Bits a second, as the station says it; 0 where it does not.</summary>
    public int Bitrate { get; private set; }

    /// <summary>No more samples come than those it has.</summary>
    public bool HasEnded => Volatile.Read(ref _hasEnded);

    /// <summary>Why it ended; null while it goes on, or where it was disposed.</summary>
    public string Problem => Volatile.Read(ref _problem);

    /// <summary>It has enough to begin playing without a gap, or ended.</summary>
    public bool IsReady => HasEnded ||
        (Volatile.Read(ref _readyFrames) is > 0 and var ready && Volatile.Read(ref _written) - Volatile.Read(ref _read) >= ready);

    /// <summary>Begins connecting, once those told of it listen.</summary>
    public void Start() => _thread.Start();

    /// <summary>The frames it has that were not taken, as many as fit; how many. Of the voice's thread, which waits for
    /// nothing.</summary>
    public int Take(Span<float> samples)
    {
        var buffer = Volatile.Read(ref _samples);
        if (buffer == null)
        {
            return 0;
        }

        var channels = Format.Channels;
        var read = Volatile.Read(ref _read);
        var frames = (int)Math.Min(samples.Length / channels, Volatile.Read(ref _written) - read);
        for (var done = 0; done < frames;)
        {
            var at = (int)((read + done) & Capacity - 1);
            var run = Math.Min(frames - done, Capacity - at);
            buffer.AsSpan(at * channels, run * channels).CopyTo(samples[(done * channels)..]);
            done += run;
        }

        Volatile.Write(ref _read, read + frames);
        return frames;
    }

    /// <summary>Stops connecting and reading; its thread lets the connection go as it leaves, not waited for - one that
    /// tells of it may be waiting for the one that disposes it. Told nothing more.</summary>
    public void Dispose()
    {
        Began = null;
        TitleChanged = null;
        Ended = null;
        _cancellation.Cancel();
        // Never started, its thread will not leave.
        if ((_thread.ThreadState & ThreadState.Unstarted) != 0)
        {
            LetCancellationGo();
        }

        LetCancellationGo();
    }

    // By whichever is last of the thread and Dispose: the thread waits on its token, and Dispose cancels it.
    private void LetCancellationGo()
    {
        if (Interlocked.Increment(ref _leaving) == 2)
        {
            _cancellation.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            ReadAll(_cancellation.Token);
        }
        finally
        {
            LetCancellationGo();
        }
    }

    // Connects, reads, connects again after a drop, until it is disposed or will not come back.
    private void ReadAll(CancellationToken token)
    {
        var tries = 0;
        var problem = (string)null;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var connection = LiveConnection.Open(_address, title => TitleChanged?.Invoke(this, title), token);
                using var decoder = SoundFile.OpenForward(connection.Sound);
                if (!Begin(decoder, connection, out problem))
                {
                    break;
                }

                // It came back: the next drop has all its tries.
                tries = 0;
                ReadOn(decoder, token);
                problem = "The station stopped sending.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or
                OperationCanceledException)
            {
                problem = e is OperationCanceledException ? "The station did not answer." : e.Message;
                if (e is InvalidDataException && Format == default)
                {
                    // What it sends is no sound that plays: connecting again sends the same.
                    break;
                }
            }

            if (token.IsCancellationRequested || tries == Waits.Count || token.WaitHandle.WaitOne(Waits[tries++]))
            {
                break;
            }
        }

        if (!token.IsCancellationRequested)
        {
            Volatile.Write(ref _problem, problem);
            Volatile.Write(ref _hasEnded, true);
            Ended?.Invoke(this);
        }
    }

    // The first connection says the format; one again must say the same, as the voice plays on in it.
    private bool Begin(ISoundDecoder decoder, LiveConnection connection, out string problem)
    {
        problem = null;
        if (Format == default)
        {
            Format = decoder.Format;
            Codec = decoder.Codec;
            Station = connection.Station;
            Bitrate = connection.Bitrate;
            Volatile.Write(ref _readyFrames, (int)Math.Min(Format.FramesIn(ReadyAfter), Capacity / 2));
            Volatile.Write(ref _samples, new float[Capacity * Format.Channels]);
            Began?.Invoke(this);
            return true;
        }

        if (decoder.Format == Format)
        {
            return true;
        }

        problem = "The station's sound is of another format now.";
        return false;
    }

    // Decodes what comes into the samples, waiting for room as they are taken, until the stream ends.
    private void ReadOn(ISoundDecoder decoder, CancellationToken token)
    {
        var channels = Format.Channels;
        var chunk = new float[ChunkFrames * channels];
        while (!token.IsCancellationRequested)
        {
            while (Capacity - (_written - Volatile.Read(ref _read)) < ChunkFrames)
            {
                if (token.WaitHandle.WaitOne(RoomWait))
                {
                    return;
                }
            }

            var got = decoder.Read(chunk);
            if (got == 0)
            {
                return;
            }

            for (var done = 0; done < got;)
            {
                var at = (int)((_written + done) & Capacity - 1);
                var run = Math.Min(got - done, Capacity - at);
                chunk.AsSpan(done * channels, run * channels).CopyTo(_samples.AsSpan(at * channels));
                done += run;
            }

            Volatile.Write(ref _written, _written + got);
        }
    }
}
