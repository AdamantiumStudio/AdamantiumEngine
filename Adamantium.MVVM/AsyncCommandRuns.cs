using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Adamantium.MVVM;

/// <summary>The runs of an async command under way, each with its own cancellation: one at a time, or as many as are
/// started when the command allows concurrent runs.</summary>
internal sealed class AsyncCommandRuns
{
    private readonly HashSet<CancellationTokenSource> _running = new();
    private readonly Action _runningChanged;

    public AsyncCommandRuns(bool allowsConcurrentRuns, Action runningChanged)
    {
        AllowsConcurrentRuns = allowsConcurrentRuns;
        _runningChanged = runningChanged;
    }

    public bool AllowsConcurrentRuns { get; }

    public bool IsRunning => _running.Count > 0;

    /// <summary>True when a run under way keeps another from starting.</summary>
    public bool Blocks => IsRunning && !AllowsConcurrentRuns;

    public CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running.Add(run);
        if (_running.Count == 1)
        {
            _runningChanged();
        }

        return run;
    }

    public void End(CancellationTokenSource run)
    {
        _running.Remove(run);
        run.Dispose();
        if (_running.Count == 0)
        {
            _runningChanged();
        }
    }

    // A copy: a cancelled run may end at once, while the runs are being cancelled.
    public void CancelAll()
    {
        foreach (var run in _running.ToList())
        {
            run.Cancel();
        }
    }
}
