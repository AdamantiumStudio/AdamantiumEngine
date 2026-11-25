using Adamantium.Pulsar.Outputs;

namespace Adamantium.Pulsar;

/// <summary>The outputs of this system.</summary>
public static class AudioOutput
{
    /// <summary>The default device of the system, keeping at least <paramref name="buffer"/> of sound ready: more rides out
    /// longer pauses, less answers sooner.</summary>
    /// <exception cref="PlatformNotSupportedException">No device output is written for this system yet.</exception>
    public static IAudioOutput CreateDefault(TimeSpan buffer)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(buffer, TimeSpan.Zero);
        return OperatingSystem.IsWindows()
            ? new WasapiOutput(AudioFormat.Default, buffer)
            : throw new PlatformNotSupportedException("Pulsar plays to no device of this system yet.");
    }

    /// <summary>No device: the renderer is called by the clock, as a device would call it, and what it makes is dropped.
    /// </summary>
    public static IAudioOutput CreateSilent(TimeSpan buffer)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(buffer, TimeSpan.Zero);
        return new ClockOutput(AudioFormat.Default, buffer);
    }
}
