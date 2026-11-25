using System.Diagnostics;

namespace Adamantium.Pulsar.Outputs;

/// <summary>An output to the default device: opened again when it changes, and the clock's while there is none, so that
/// what is played keeps its time and is heard from where it got to once a device comes.</summary>
internal abstract class DeviceOutput(AudioFormat format, TimeSpan buffer) : ThreadedOutput(format)
{
    private static readonly TimeSpan BetweenTries = TimeSpan.FromSeconds(1);

    // One for all the time without a device: each begins a buffer ahead.
    private ClockSchedule _silence;
    private float[] _silentSamples;
    private long _silentSince;
    private long _playingSince;

    /// <summary>How much sound to keep ready.</summary>
    protected TimeSpan Wanted { get; } = buffer;

    protected bool IsStopping => Stopping.WaitOne(0);

    protected sealed override void Play()
    {
        _silence = null;
        while (!IsStopping)
        {
            _playingSince = 0;
            ForgetLastFill();
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
                // One that played is gone: the system may have another already. One that fails at once is waited for.
                if (_playingSince == 0 || Stopwatch.GetElapsedTime(_playingSince) < BetweenTries)
                {
                    PlayByClock();
                }
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
        _silence = null;
        _playingSince = Stopwatch.GetTimestamp();
        DeviceProblem = null;
        Opened(bufferFrames, AudioOutputState.Running);
    }

    private void PlayByClock()
    {
        if (_silence == null)
        {
            _silence = new ClockSchedule(Format, Wanted);
            _silentSamples = new float[_silence.PeriodFrames * Format.Channels];
            _silentSince = Stopwatch.GetTimestamp();
            Opened(_silence.BufferFrames, AudioOutputState.Silent);
        }

        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < BetweenTries)
        {
            var periods = _silence.PeriodsAt(Stopwatch.GetElapsedTime(_silentSince), out _);
            for (var period = 0; period < periods; period++)
            {
                Render(_silentSamples);
            }

            if (Stopping.WaitOne(_silence.WaitAt(Stopwatch.GetElapsedTime(_silentSince))))
            {
                return;
            }
        }
    }
}
