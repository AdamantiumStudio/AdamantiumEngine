using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

// Its own thread: the caller hands over the commands asked and reads the runs done at each Update; the thread waits
// for its next step, not spinning, and waits while too many runs lie unread.
public sealed partial class PhysicsSimulation
{
    // Under _gate: the commands handed over and not yet run, the runs done and not yet read, and the spare ones.
    private readonly object _gate = new();
    private readonly List<SimulationCommand> _handedCommands = [];
    private readonly Queue<SimulationResult> _readyResults = new();
    private readonly Stack<SimulationResult> _spareResults = new();
    // The thread's own: the commands it took over to run.
    private readonly List<SimulationCommand> _threadCommands = [];
    private readonly AutoResetEvent _wake = new(false);
    private Thread _thread;
    private volatile bool _stopping;
    private volatile bool _paused;
    private volatile Exception _failure;
    // The caller's count of batches of commands handed over: a slot taken out is free once its batch was run.
    private long _handedBatches;
    private long _handedBatchesUnderGate;
    private int _unreadResults;
    private int _lastResultSteps = 1;
    private long _lastResultAt;

    /// <summary>Steps on a thread of its own from now on, at <see cref="PhysicsSettings.StepRate"/>; paused, it goes
    /// on. What it does is read at each <see cref="Update"/>.</summary>
    public void Start()
    {
        ThrowIfDisposed();
        if (State == SimulationState.Running)
            return;

        if (State == SimulationState.Paused)
        {
            _paused = false;
            State = SimulationState.Running;
            _wake.Set();
            return;
        }

        _snapshot = TakeSnapshot();
        _stopping = false;
        _paused = false;
        _failure = null;
        // Its first run tells the last batch run; those handed over and put back by a Stop go again as the next.
        _handedBatchesUnderGate = _batchesRead;
        _lastResultAt = Stopwatch.GetTimestamp();
        State = SimulationState.Running;
        _thread = new Thread(RunThread, ThreadStackSize) { IsBackground = true, Name = "DarkMatter simulation" };
        _thread.Start();
    }

    /// <summary>Holds its thread: nothing moves until it is started again, but for a step already under way, which is
    /// read at the next <see cref="Update"/>.</summary>
    public void Pause()
    {
        ThrowIfDisposed();
        if (State != SimulationState.Running)
            return;

        _paused = true;
        State = SimulationState.Paused;
        _wake.Set();
    }

    /// <summary>Ends its thread, what it did read; it steps by hand again, the actions asked and not yet done
    /// waiting for the next step.</summary>
    public void Stop()
    {
        ThrowIfDisposed();
        StopThread();
    }

    /// <summary>Once a frame while it steps on its own thread: hands over what was asked since, and brings what the
    /// caller reads up to the last step done. Throws what stopped the thread, if something did.</summary>
    public void Update()
    {
        ThrowIfDisposed();
        if (State == SimulationState.Stopped)
            return;

        HandOver();
        ReadResults();
        if (_failure is { } failure)
        {
            StopThread();
            throw new InvalidOperationException("The simulation stopped stepping.", failure);
        }
    }

    private void HandOver()
    {
        if (_commands.Count == 0)
            return;

        _handedBatches++;
        lock (_gate)
        {
            _handedCommands.AddRange(_commands);
            _handedBatchesUnderGate = _handedBatches;
        }

        _commands.Clear();
    }

    private void ReadResults()
    {
        var read = false;
        _snapshotMark++;
        while (true)
        {
            SimulationResult result;
            lock (_gate)
            {
                if (!_readyResults.TryDequeue(out result))
                    break;
            }

            if (!read)
                ForgetRefusals();

            read = true;
            Read(result);
            _lastResultSteps = result.Steps;
            _lastResultAt = result.SteppedAt;
            lock (_gate)
            {
                _spareResults.Push(result);
                _unreadResults--;
            }
        }

        if (!read)
            return;

        if (_snapshot != null)
            MoveSnapshot();

        // Room again for a thread that waited on the unread runs.
        _wake.Set();
    }

    private void StopThread()
    {
        if (_thread == null)
            return;

        _stopping = true;
        _wake.Set();
        _thread.Join();
        _thread = null;
        ReadResults();
        // What it took over and had not run yet goes first, then what was handed over, then what was asked since.
        lock (_gate)
        {
            _threadCommands.AddRange(_handedCommands);
            _handedCommands.Clear();
        }

        _threadCommands.AddRange(_commands);
        _commands.Clear();
        _commands.AddRange(_threadCommands);
        _threadCommands.Clear();
        _snapshot = null;
        State = SimulationState.Stopped;
    }

    private void RunThread()
    {
        try
        {
            var clock = new StepClock(Settings.StepRate, MaxStepsPerAdvance);
            var previous = Stopwatch.GetTimestamp();
            while (!_stopping)
            {
                if (_paused)
                {
                    _wake.WaitOne();
                    clock.Reset();
                    previous = Stopwatch.GetTimestamp();
                    continue;
                }

                if (Volatile.Read(ref _unreadResults) >= MaxUnreadResults)
                {
                    _wake.WaitOne();
                    continue;
                }

                var now = Stopwatch.GetTimestamp();
                var steps = clock.Advance(Stopwatch.GetElapsedTime(previous, now).TotalSeconds);
                previous = now;
                if (steps == 0)
                {
                    WaitForNextStep(clock);
                    continue;
                }

                RunOnThread(steps);
            }
        }
        catch (Exception exception)
        {
            _failure = exception;
        }
    }

    private void RunOnThread(int steps)
    {
        SimulationResult result;
        long batch;
        lock (_gate)
        {
            _threadCommands.AddRange(_handedCommands);
            _handedCommands.Clear();
            batch = _handedBatchesUnderGate;
            result = _spareResults.Count > 0 ? _spareResults.Pop() : new SimulationResult();
        }

        _stepper.Run(_threadCommands, steps, batch, result);
        _threadCommands.Clear();
        lock (_gate)
        {
            _readyResults.Enqueue(result);
            _unreadResults++;
        }
    }

    private void WaitForNextStep(StepClock clock)
    {
        var remaining = (1 - clock.Blend) * clock.StepSeconds * 1000;
        if (remaining >= 1)
            _wake.WaitOne((int)remaining);
        else
            Thread.Yield();
    }

    // How far between its last two reads to draw: through the steps the last run took, by the time since it.
    private float MeasureBlend()
    {
        double sinceStep = State switch
        {
            SimulationState.Running => Stopwatch.GetElapsedTime(_lastResultAt).TotalSeconds / StepSeconds,
            SimulationState.Paused => 1,
            _ => _blendsByClock ? _clock.Blend : 1
        };
        return (float)Math.Clamp((_lastResultSteps - 1 + sinceStep) / _lastResultSteps, 0, 1);
    }
}
