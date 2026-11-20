using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

// Its own thread, talked to through channels: the caller sends the commands asked as a batch at each Update and reads
// the runs the thread sends back; each side gives back what it read through a channel of spares, to be filled again.
// The thread waits for its next step, not spinning, and waits while too many runs lie unread.
public sealed partial class PhysicsSimulation
{
    private static readonly UnboundedChannelOptions OneWriterOneReader = new()
    {
        SingleWriter = true, SingleReader = true
    };

    // The caller to the thread: the batches of commands; the thread to the caller: the runs done.
    private readonly Channel<CommandBatch> _batches = Channel.CreateUnbounded<CommandBatch>(OneWriterOneReader);
    private readonly Channel<SimulationResult> _results = Channel.CreateUnbounded<SimulationResult>(OneWriterOneReader);
    // What each side read, given back to the other to fill again.
    private readonly Channel<CommandBatch> _spareBatches = Channel.CreateUnbounded<CommandBatch>(OneWriterOneReader);
    private readonly Channel<SimulationResult> _spareResults =
        Channel.CreateUnbounded<SimulationResult>(OneWriterOneReader);
    // The thread's own: the commands it took over to run, and the number of the last batch among them.
    private readonly List<SimulationCommand> _threadCommands = [];
    private long _threadBatch;
    // Wakes the thread: a channel wakes only an asynchronous reader, and the thread is not one.
    private readonly AutoResetEvent _wake = new(false);
    // Tells a Pause the thread holds: a run under way when it was asked has ended.
    private readonly ManualResetEventSlim _parked = new(false);
    private Thread _thread;
    private volatile bool _stopping;
    private volatile bool _paused;
    private volatile Exception _failure;
    // The caller's count of batches of commands handed over: a slot taken out is free once its batch was run.
    private long _handedBatches;
    private int _unreadResults;
    private int _lastResultSteps = 1;
    private long _lastResultAt;
    private long _threadAllocatedBytes;

    // What its thread allocated, as of its last run: for the tests to see that it allocates none.
    internal long ThreadAllocatedBytes => Volatile.Read(ref _threadAllocatedBytes);

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
        // Its first run tells the last batch run; those put back by a Stop go again as the next.
        _threadBatch = _batchesRead;
        _lastResultAt = Stopwatch.GetTimestamp();
        State = SimulationState.Running;
        _thread = new Thread(RunThread, ThreadStackSize) { IsBackground = true, Name = "DarkMatter simulation" };
        _thread.Start();
    }

    /// <summary>Holds its thread: nothing moves until it is started again. A step already under way is waited for, and
    /// read at the next <see cref="Update"/>.</summary>
    public void Pause()
    {
        ThrowIfDisposed();
        if (State != SimulationState.Running)
            return;

        _parked.Reset();
        _paused = true;
        State = SimulationState.Paused;
        _wake.Set();
        // A thread that failed holds already; its failure is thrown at the next Update.
        if (_failure == null)
            _parked.Wait();
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

        // All the batches with the thread, it took none of them yet: these wait for the next Update, to run as soon.
        if (!_spareBatches.Reader.TryRead(out var batch))
            return;

        batch.Commands.AddRange(_commands);
        batch.Number = ++_handedBatches;
        _batches.Writer.TryWrite(batch);
        _commands.Clear();
    }

    private void ReadResults()
    {
        var read = false;
        _snapshotMark++;
        while (_results.Reader.TryRead(out var result))
        {
            if (!read)
                ForgetRefusals();

            read = true;
            Read(result);
            _lastResultSteps = result.Steps;
            _lastResultAt = result.SteppedAt;
            _spareResults.Writer.TryWrite(result);
            Interlocked.Decrement(ref _unreadResults);
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
        // The thread has ended: its side is the caller's now. What it took over and had not run yet goes first, then
        // the batches it did not take, then what was asked since.
        TakeBatches();
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
                    _parked.Set();
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
        finally
        {
            // Ended, it holds: a Pause does not wait for it.
            _parked.Set();
        }
    }

    private void RunOnThread(int steps)
    {
        TakeBatches();
        if (!_spareResults.Reader.TryRead(out var result))
            result = new SimulationResult();

        _stepper.Run(_threadCommands, steps, _threadBatch, result);
        _threadCommands.Clear();
        Interlocked.Increment(ref _unreadResults);
        _results.Writer.TryWrite(result);
        Volatile.Write(ref _threadAllocatedBytes, GC.GetAllocatedBytesForCurrentThread());
    }

    // The batches sent, their commands taken over in order, each batch given back.
    private void TakeBatches()
    {
        while (_batches.Reader.TryRead(out var batch))
        {
            _threadCommands.AddRange(batch.Commands);
            _threadBatch = batch.Number;
            batch.Commands.Clear();
            _spareBatches.Writer.TryWrite(batch);
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
