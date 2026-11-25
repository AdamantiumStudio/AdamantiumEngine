using System.Runtime.InteropServices;
using Adamantium.MacOS.AudioToolbox;

namespace Adamantium.Pulsar.Outputs;

/// <summary>The default output device of macOS: buffers queued with the system, each rendered anew once it was played. The
/// queue follows the default device by itself.</summary>
internal sealed unsafe class AudioQueueOutput(AudioFormat format, TimeSpan buffer) : DeviceOutput(format, buffer)
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(10);
    // How soon a stop is heard while nothing comes.
    private const double LoopSeconds = 0.05;

    private IntPtr _queue;
    private AudioQueueBuffer*[] _played;
    private int _playedCount;

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
            while (!IsStopping)
            {
                AudioQueue.RunLoop(mode, LoopSeconds, 1);
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
        _played = new AudioQueueBuffer*[Math.Max(2, (int)Math.Ceiling(Wanted / Period))];
        _playedCount = 0;
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

        var ranDry = _playedCount == _played.Length;
        while (_playedCount != 0)
        {
            var buffer = _played[_playedCount - 1];
            Render(new Span<float>(buffer->Data, (int)buffer->Capacity / sizeof(float)));
            buffer->Size = buffer->Capacity;
            Check(AudioQueue.Enqueue(_queue, buffer, 0, IntPtr.Zero), "queue a buffer");
            _playedCount--;
        }

        Filled(ranDry);
    }

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
