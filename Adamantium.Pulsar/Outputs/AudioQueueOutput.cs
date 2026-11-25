using System.Diagnostics;
using System.Runtime.InteropServices;
using Adamantium.MacOS.AudioToolbox;

namespace Adamantium.Pulsar.Outputs;

/// <summary>The default output device of macOS: buffers queued with the system, each rendered anew once it was played. The
/// queue follows the default device by itself.</summary>
internal sealed unsafe class AudioQueueOutput(AudioFormat format, TimeSpan buffer) : DeviceOutput(format, buffer)
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(10);
    // With two the queue broke off six times a second, measured; with four never.
    private const int LeastBuffers = 4;
    // A queue whose device is gone says nothing: it only gives no buffer back.
    private static readonly TimeSpan Standstill = TimeSpan.FromSeconds(2);
    // How soon a stop is heard while nothing comes.
    private const double LoopSeconds = 0.05;

    private IntPtr _queue;
    private AudioQueueBuffer*[] _played;
    private int _playedCount;
    private long _queuedFrames;
    private bool _silent;

    protected override void PlayToDevice()
    {
        var self = GCHandle.Alloc(this);
        try
        {
            var mode = AudioQueue.DefaultMode();
            var periodFrames = Format.FramesIn(Period);
            Open(GCHandle.ToIntPtr(self), mode, periodFrames);
            Fill();
            Check(AudioQueue.Start(_queue, IntPtr.Zero), "start the queue");
            PlaysToDevice(periodFrames * _played.Length);
            var lastPlayed = Stopwatch.GetTimestamp();
            while (!IsStopping)
            {
                AudioQueue.RunLoop(mode, LoopSeconds, 1);
                if (_playedCount != 0)
                {
                    lastPlayed = Stopwatch.GetTimestamp();
                }
                else if (Stopwatch.GetElapsedTime(lastPlayed) > Wanted + Standstill)
                {
                    throw new DeviceException("The audio device stopped playing.");
                }

                Fill();
            }
        }
        finally
        {
            // The queue gives nothing back after it is gone: the handle outlives it.
            CloseDevice();
            self.Free();
        }
    }

    protected override void CloseDevice()
    {
        if (_queue != IntPtr.Zero)
        {
            AudioQueue.Dispose(_queue, 1);
            _queue = IntPtr.Zero;
        }
    }

    private void Open(IntPtr self, IntPtr mode, long periodFrames)
    {
        var description = AudioStreamDescription.Float(Format.SampleRate, Format.Channels);
        Check(AudioQueue.CreateOutput(description, &OnPlayed, self, AudioQueue.CurrentRunLoop(), mode, 0, out _queue),
            "open a queue");
        _played = new AudioQueueBuffer*[Math.Max(LeastBuffers, (int)Math.Ceiling(Wanted / Period))];
        _playedCount = 0;
        _queuedFrames = 0;
        _silent = false;
        for (var index = 0; index < _played.Length; index++)
        {
            Check(AudioQueue.AllocateBuffer(_queue, (uint)(periodFrames * Format.Channels * sizeof(float)), out var made),
                "make a buffer");
            _played[_playedCount++] = made;
        }
    }

    private void Fill()
    {
        if (_playedCount == 0)
        {
            return;
        }

        // Late, the queue drops what it is given until it catches up with its own time: one silence until then.
        var silent = _playedCount == _played.Length || IsBehind();
        var ranDry = silent && !_silent;
        _silent = silent;
        while (_playedCount != 0)
        {
            var buffer = _played[_playedCount - 1];
            Render(new Span<float>(buffer->Data, (int)buffer->Capacity / sizeof(float)));
            buffer->Size = buffer->Capacity;
            Check(AudioQueue.Enqueue(_queue, buffer, 0, IntPtr.Zero), "queue a buffer");
            _playedCount--;
            _queuedFrames += buffer->Size / (Format.Channels * sizeof(float));
        }

        Filled(ranDry);
    }

    private bool IsBehind() =>
        AudioQueue.GetTime(_queue, IntPtr.Zero, out var time, IntPtr.Zero) == 0 && time.SampleTime > _queuedFrames;

    // Called in RunLoop, on the output's own thread.
    [UnmanagedCallersOnly]
    private static void OnPlayed(IntPtr userData, IntPtr queue, AudioQueueBuffer* buffer)
    {
        var output = (AudioQueueOutput)GCHandle.FromIntPtr(userData).Target;
        output._played[output._playedCount++] = buffer;
    }

    private static void Check(int status, string doing)
    {
        if (status != 0)
        {
            throw new DeviceException($"Could not {doing} ({status}).");
        }
    }
}
