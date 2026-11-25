using System.Diagnostics;

namespace Adamantium.Pulsar.Outputs;

/// <summary>An output to the default device: opened again when it changes, and the clock's while there is none, so that
/// what is played keeps its time and is heard from where it got to once a device comes.</summary>
internal abstract class DeviceOutput(AudioFormat format, TimeSpan buffer) : ThreadedOutput(format)
{
    private static readonly TimeSpan BetweenTries = TimeSpan.FromSeconds(1);

    /// <summary>How much sound to keep ready.</summary>
    protected TimeSpan Wanted { get; } = buffer;

    protected bool IsStopping => Stopping.WaitOne(0);

    protected sealed override void Play()
    {
        while (!IsStopping)
        {
            try
            {
                try
                {
                    PlayToDevice();
                }
                finally
                {
                    CloseDevice();
                }
            }
            catch (DeviceException e)
            {
                DeviceProblem = e.Message;
                PlayByClock();
            }
        }
    }

    /// <summary>Opens the device, says it plays, and plays until <see cref="ThreadedOutput.Stopping"/> or until another
    /// device is to be opened.</summary>
    /// <exception cref="DeviceException">There is none, or it went away.</exception>
    protected abstract void PlayToDevice();

    /// <summary>Closes what was opened, all of it or not.</summary>
    protected abstract void CloseDevice();

    protected void PlaysToDevice(long bufferFrames)
    {
        DeviceProblem = null;
        Opened(bufferFrames, AudioOutputState.Running);
    }

    private void PlayByClock()
    {
        var schedule = new ClockSchedule(Format, Wanted);
        var samples = new float[schedule.PeriodFrames * Format.Channels];
        Opened(schedule.BufferFrames, AudioOutputState.Silent);
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < BetweenTries)
        {
            var periods = schedule.PeriodsAt(Stopwatch.GetElapsedTime(started), out _);
            for (var period = 0; period < periods; period++)
            {
                Render(samples);
            }

            if (Stopping.WaitOne(schedule.WaitAt(Stopwatch.GetElapsedTime(started))))
            {
                return;
            }
        }
    }
}
