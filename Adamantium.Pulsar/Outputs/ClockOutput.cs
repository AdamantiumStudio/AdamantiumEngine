using System.Diagnostics;

namespace Adamantium.Pulsar.Outputs;

/// <summary>No device: asks for as much sound as the clock says was played, and drops it.</summary>
internal sealed class ClockOutput(AudioFormat format, TimeSpan buffer) : ThreadedOutput(format)
{
    protected override void Play()
    {
        var schedule = new ClockSchedule(Format, buffer);
        var samples = new float[schedule.PeriodFrames * Format.Channels];
        var started = Stopwatch.GetTimestamp();
        Opened(schedule.BufferFrames, AudioOutputState.Running);
        do
        {
            var periods = schedule.PeriodsAt(Stopwatch.GetElapsedTime(started), out var ranDry);
            for (var period = 0; period < periods; period++)
            {
                Render(samples);
            }

            if (periods != 0)
            {
                Filled(ranDry);
            }
        }
        while (!Stopping.WaitOne(schedule.WaitAt(Stopwatch.GetElapsedTime(started))));
    }
}
