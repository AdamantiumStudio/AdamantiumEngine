using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Adamantium.Pulsar.Outputs;

/// <summary>An output with a thread of its own: its start and stop, its failure and what it measures.</summary>
internal abstract class ThreadedOutput(AudioFormat format) : IAudioOutput
{
    private readonly object _lock = new();
    private readonly ManualResetEvent _stopping = new(false);
    private readonly ManualResetEventSlim _opened = new(false);
    private Thread _thread;
    private ExceptionDispatchInfo _failure;
    private int _state;
    private int _threadId;
    private bool _disposed;
    private long _bufferFrames;
    private long _framesRendered;
    private long _gaps;
    private long _longestBetweenFills;
    private long _longestRender;
    private long _lastFill;

    public AudioFormat Format { get; } = format;

    public AudioOutputState State => (AudioOutputState)Volatile.Read(ref _state);

    public TimeSpan Buffer => Format.DurationOf(Volatile.Read(ref _bufferFrames));

    public AudioOutputStatistics Statistics => new(Volatile.Read(ref _framesRendered), Volatile.Read(ref _gaps),
        Stopwatch.GetElapsedTime(0, Volatile.Read(ref _longestBetweenFills)),
        Stopwatch.GetElapsedTime(0, Volatile.Read(ref _longestRender)));

    /// <summary>Signalled to end <see cref="Play"/>.</summary>
    protected WaitHandle Stopping => _stopping;

    protected IAudioRenderer Renderer { get; private set; }

    public void Start(IAudioRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread != null)
            {
                throw new InvalidOperationException("The output is started already.");
            }

            Renderer = renderer;
            _failure = null;
            _framesRendered = _gaps = _longestBetweenFills = _longestRender = _lastFill = 0;
            _stopping.Reset();
            _opened.Reset();
            _thread = new Thread(Run) { Name = "Pulsar output", IsBackground = true, Priority = ThreadPriority.Highest };
            _thread.Start();
            _opened.Wait();
            if (State != AudioOutputState.Running && _failure != null)
            {
                End();
            }
        }
    }

    public void Stop()
    {
        RefuseFromItsThread();
        lock (_lock)
        {
            End();
        }
    }

    public void Dispose()
    {
        RefuseFromItsThread();
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                End();
            }
            catch (Exception e)
            {
                // Told by State; disposing throws nothing.
                Trace.TraceWarning("The audio output had failed: {0}", e.Message);
            }

            _stopping.Dispose();
            _opened.Dispose();
        }
    }

    /// <summary>Opens the device, says <see cref="Opened"/>, and plays until <see cref="Stopping"/>; on the output's thread.
    /// </summary>
    protected abstract void Play();

    protected void Opened(long bufferFrames)
    {
        Volatile.Write(ref _bufferFrames, bufferFrames);
        Volatile.Write(ref _state, (int)AudioOutputState.Running);
        _opened.Set();
    }

    protected void Render(Span<float> samples)
    {
        var before = Stopwatch.GetTimestamp();
        Renderer.Render(samples, Format);
        var after = Stopwatch.GetTimestamp();
        Raise(ref _longestRender, after - before);
        Volatile.Write(ref _framesRendered, _framesRendered + samples.Length / Format.Channels);
    }

    /// <summary>The device was given more; <paramref name="ranDry"/> where it had played all it had.</summary>
    protected void Filled(bool ranDry)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastFill != 0)
        {
            Raise(ref _longestBetweenFills, now - _lastFill);
            if (ranDry)
            {
                Volatile.Write(ref _gaps, _gaps + 1);
            }
        }

        _lastFill = now;
    }

    private static void Raise(ref long longest, long value)
    {
        if (value > longest)
        {
            Volatile.Write(ref longest, value);
        }
    }

    // Before the lock: whoever holds it waits for this thread.
    private void RefuseFromItsThread()
    {
        if (Volatile.Read(ref _threadId) == Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The output can not be stopped from its own renderer.");
        }
    }

    private void Run()
    {
        Volatile.Write(ref _threadId, Environment.CurrentManagedThreadId);
        try
        {
            Play();
            Volatile.Write(ref _state, (int)AudioOutputState.Stopped);
        }
        catch (Exception e)
        {
            _failure = ExceptionDispatchInfo.Capture(e);
            Volatile.Write(ref _state, (int)AudioOutputState.Failed);
        }
        finally
        {
            // The id may be another thread's once this one ends.
            Volatile.Write(ref _threadId, 0);
            _opened.Set();
        }
    }

    private void End()
    {
        if (_thread == null)
        {
            return;
        }

        _stopping.Set();
        _thread.Join();
        _thread = null;
        Renderer = null;
        Volatile.Write(ref _bufferFrames, 0);
        var failure = _failure;
        _failure = null;
        failure?.Throw();
    }
}
