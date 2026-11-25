using System.Diagnostics;

namespace Adamantium.Pulsar.Mixing;

/// <summary>Reads the streamed sounds that play, on a thread of its own, begun with the first of them.</summary>
internal sealed class SoundStreamer : IDisposable
{
    private readonly Lock _lock = new();
    private readonly AutoResetEvent _wanted = new(false);
    private readonly List<SoundStream> _added = [];
    private readonly List<SoundStream> _streams = [];
    // Of one channel and of two, at their number's place.
    private readonly Stack<float[]>[] _spare = [null, new(), new()];
    private Thread _thread;
    private bool _stopping;
    private string _problem;
    private int _wakes;
    private int _wakesDone;

    /// <summary>Why a sound was last cut off.</summary>
    public string Problem => Volatile.Read(ref _problem);

    public void Add(SoundStream stream)
    {
        lock (_lock)
        {
            _added.Add(stream);
            if (_thread == null)
            {
                _thread = new Thread(Run) { Name = "Pulsar streams", IsBackground = true, Priority = ThreadPriority.AboveNormal };
                _thread.Start();
            }
        }

        Wake();
    }

    /// <summary>Some stream wants more, or is done with. From the output's thread too: it waits for nothing.</summary>
    public void Wake()
    {
        Interlocked.Increment(ref _wakes);
        _wanted.Set();
    }

    /// <summary>All that was wanted is read.</summary>
    public bool IsIdle => Volatile.Read(ref _wakesDone) == Volatile.Read(ref _wakes);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
        }

        _wanted.Set();
        _thread?.Join();
    }

    private void Run()
    {
        while (true)
        {
            var wakes = Volatile.Read(ref _wakes);
            if (!TakeAdded())
            {
                break;
            }

            // A part of each in turn: none waits for another to be filled.
            var filled = false;
            for (var index = _streams.Count - 1; index >= 0; index--)
            {
                var stream = _streams[index];
                if (stream.IsReleased)
                {
                    _streams.RemoveAt(index);
                    stream.Close();
                    KeepSpare(stream);
                }
                else
                {
                    filled |= Fill(stream);
                }
            }

            if (!filled)
            {
                Volatile.Write(ref _wakesDone, wakes);
                _wanted.WaitOne();
            }
        }

        foreach (var stream in _streams)
        {
            stream.Finish();
        }
    }

    // False where the engine is disposed.
    private bool TakeAdded()
    {
        lock (_lock)
        {
            _streams.AddRange(_added);
            _added.Clear();
            return !_stopping;
        }
    }

    private bool Fill(SoundStream stream)
    {
        if (stream.HasAll)
        {
            return false;
        }

        try
        {
            return stream.Fill(stream.Samples ?? TakeSpare(stream.Format.Channels));
        }
        catch (Exception e)
        {
            // Whatever a file or its storage does, the others play on.
            Trace.TraceWarning("A streamed sound was cut off: {0}", e.Message);
            Volatile.Write(ref _problem, e.Message);
            stream.Finish();
            return false;
        }
    }

    private float[] TakeSpare(int channels) =>
        _spare[channels].TryPop(out var samples) ? samples : new float[SoundStream.Capacity * channels];

    private void KeepSpare(SoundStream stream)
    {
        if (stream.Samples != null)
        {
            _spare[stream.Format.Channels].Push(stream.Samples);
        }
    }
}
