using System;
using System.Diagnostics;
using System.Threading;

namespace Adamantium.Graphics.Core;

/// <summary>The consumer's frames, counted as it asks for each, so a producer in the same process makes one frame per
/// frame shown. A consumer in another process never raises it.</summary>
public sealed class SharedSurfaceDemand
{
    private readonly object _sync = new();
    private ulong _count;
    private Action _onRaised;

    /// <summary>How many frames the consumer has asked for.</summary>
    public ulong Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    /// <summary>For the consumer: asks for a frame.</summary>
    public void Raise()
    {
        Action onRaised;
        lock (_sync)
        {
            _count++;
            Monitor.PulseAll(_sync);
            onRaised = _onRaised;
            _onRaised = null;
        }

        onRaised?.Invoke();
    }

    /// <summary>For the producer: runs <paramref name="action"/> once the consumer has asked for more than
    /// <paramref name="seen"/> frames - at once if it already has, else on the consumer's thread when it asks. One action
    /// waits at a time; a new one takes the place of the last.</summary>
    public void WhenBeyond(ulong seen, Action action)
    {
        lock (_sync)
        {
            if (_count <= seen)
            {
                _onRaised = action;
                return;
            }
        }

        action();
    }

    /// <summary>For the producer: waits until the consumer has asked for more than <paramref name="seen"/> frames, up to
    /// <paramref name="timeout"/>; returns the count.</summary>
    public ulong WaitBeyond(ulong seen, TimeSpan timeout)
    {
        var start = Stopwatch.GetTimestamp();
        lock (_sync)
        {
            while (_count <= seen)
            {
                var left = timeout - Stopwatch.GetElapsedTime(start);
                if (left <= TimeSpan.Zero || !Monitor.Wait(_sync, left)) break;
            }

            return _count;
        }
    }
}
