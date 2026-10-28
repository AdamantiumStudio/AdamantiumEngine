using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Adamantium.ECS;

namespace Adamantium.Multiverse;

/// <summary>A universe's <see cref="IUniverseWatcher"/>, the one place that asks it for frames: it keeps the most frames
/// asked for and the latest moment to draw until, and wakes the universe once for any number of asks between its
/// frames.</summary>
internal sealed class UniverseWatcher : IUniverseWatcher
{
    private readonly Action wake;
    private readonly Lock gate = new();
    private readonly HashSet<INotifyUniverse> watching = [];
    private int framesOwed;
    private long awakeUntil;
    private int wakePending;
    private volatile bool isWatching;

    /// <summary>A watcher that calls <paramref name="wake"/> to wake its universe.</summary>
    internal UniverseWatcher(Action wake)
    {
        this.wake = wake;
    }

    /// <summary>Whether the universe draws only when asked, so there is anything to watch for. While it draws every
    /// frame, the watcher holds no subscription and every ask returns at once; what it was given to watch it starts
    /// listening to again when this turns back on.</summary>
    internal bool IsWatching
    {
        get => isWatching;
        set
        {
            lock (gate)
            {
                if (isWatching == value)
                {
                    return;
                }

                isWatching = value;
                foreach (var watched in watching)
                {
                    if (value)
                    {
                        watched.UniverseChanged += OnUniverseChanged;
                    }
                    else
                    {
                        watched.UniverseChanged -= OnUniverseChanged;
                    }
                }

                if (!value)
                {
                    framesOwed = 0;
                    awakeUntil = 0;
                }
            }
        }
    }

    public void Watch(INotifyUniverse watched)
    {
        ArgumentNullException.ThrowIfNull(watched);
        lock (gate)
        {
            if (watching.Add(watched) && isWatching)
            {
                watched.UniverseChanged += OnUniverseChanged;
            }
        }
    }

    public void ReleaseWatch(INotifyUniverse watched)
    {
        ArgumentNullException.ThrowIfNull(watched);
        lock (gate)
        {
            if (watching.Remove(watched) && isWatching)
            {
                watched.UniverseChanged -= OnUniverseChanged;
            }
        }
    }

    public void Watch() => Watch(1);

    public void Watch(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        if (!isWatching)
        {
            return;
        }

        lock (gate)
        {
            framesOwed = Math.Max(framesOwed, frames);
        }

        Wake();
    }

    public void Watch(TimeSpan duration)
    {
        if (!isWatching)
        {
            return;
        }

        var until = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        lock (gate)
        {
            awakeUntil = Math.Max(awakeUntil, until);
        }

        Wake();
    }

    /// <summary>Whether a frame is owed: frames asked for and not drawn yet, or a moment to draw until not reached.
    /// </summary>
    internal bool Owes
    {
        get
        {
            lock (gate)
            {
                return framesOwed > 0 || Stopwatch.GetTimestamp() < awakeUntil;
            }
        }
    }

    /// <summary>Takes one owed frame as the universe starts one; whether a frame was owed.</summary>
    internal bool TakeFrame()
    {
        lock (gate)
        {
            if (framesOwed > 0)
            {
                framesOwed--;
                return true;
            }

            return Stopwatch.GetTimestamp() < awakeUntil;
        }
    }

    /// <summary>The universe has taken its messages: the next ask wakes it again.</summary>
    internal void Woken() => Volatile.Write(ref wakePending, 0);

    private void OnUniverseChanged(object sender, UniverseChangedEventArgs change)
    {
        if (change.Duration > TimeSpan.Zero)
        {
            Watch(change.Duration);
        }
        else
        {
            Watch(Math.Max(change.Frames, 1));
        }
    }

    private void Wake()
    {
        if (Interlocked.Exchange(ref wakePending, 1) == 0)
        {
            wake();
        }
    }
}
