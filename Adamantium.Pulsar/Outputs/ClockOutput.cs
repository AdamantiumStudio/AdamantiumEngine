using System.Diagnostics;

namespace Adamantium.Pulsar.Outputs;

/// <summary>No device: asks for as much sound as the clock says was played, half the buffer at a time, and drops it.
/// </summary>
internal sealed class ClockOutput(AudioFormat format, TimeSpan buffer) : ThreadedOutput(format)
{
    protected override void Play()
    {
        var bufferFrames = Math.Max(2, Format.FramesIn(buffer));
        var periodFrames = bufferFrames / 2;
        var samples = new float[periodFrames * Format.Channels];
        var started = Stopwatch.GetTimestamp();
        long written = 0;
        Opened(bufferFrames);
        do
        {
            var played = Format.FramesIn(Stopwatch.GetElapsedTime(started));
            var ranDry = written < played;
            if (ranDry)
            {
                // A device left without sound plays silence meanwhile.
                written = played;
            }

            while (written - played + periodFrames <= bufferFrames)
            {
                Render(samples);
                written += periodFrames;
            }

            Filled(ranDry);
        }
        while (!Stopping.WaitOne(TimeUntil(written - bufferFrames + periodFrames, started)));
    }

    private TimeSpan TimeUntil(long frames, long started)
    {
        var left = Format.DurationOf(frames) - Stopwatch.GetElapsedTime(started);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
