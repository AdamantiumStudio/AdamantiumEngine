using System;
using System.Collections.Generic;
using System.Threading;

namespace Adamantium.MVVM;

/// <summary>The runs of an async command under way, each with its own cancellation: one at a time, or as many as are
/// started when the command allows concurrent runs. Runs may end on other threads than they started on, with no
/// context to come back to, so the set is kept under a lock.</summary>
internal sealed class AsyncCommandRuns
{
    private readonly HashSet<CancellationTokenSource> _running = new();
    private readonly object _lock = new();
    private readonly Action _runningChanged;

    public AsyncCommandRuns(bool allowsConcurrentRuns, Action runningChanged)
    {
        AllowsConcurrentRuns = allowsConcurrentRuns;
        _runningChanged = runningChanged;
    }

    public bool AllowsConcurrentRuns { get; }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _running.Count > 0;
            }
        }
    }

    /// <summary>True when a run under way keeps another from starting.</summary>
    public bool Blocks => !AllowsConcurrentRuns && IsRunning;

    public CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool first;
        lock (_lock)
        {
            _running.Add(run);
            first = _running.Count == 1;
        }

        // Told outside the lock: a handler may ask IsRunning or CanExecute.
        if (first)
        {
            _runningChanged();
        }

        return run;
    }

    public void End(CancellationTokenSource run)
    {
        bool last;
        lock (_lock)
        {
            _running.Remove(run);
            last = _running.Count == 0;
        }

        run.Dispose();
        if (last)
        {
            _runningChanged();
        }
    }

    // A copy: a cancelled run may end at once, while the runs are being cancelled.
    public void CancelAll()
    {
        CancellationTokenSource[] running;
        lock (_lock)
        {
            running = [.. _running];
        }

        foreach (var run in running)
        {
            try
            {
                run.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // That run ended meanwhile, on another thread.
            }
        }
    }
}
