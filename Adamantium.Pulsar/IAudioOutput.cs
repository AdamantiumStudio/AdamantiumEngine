namespace Adamantium.Pulsar;

/// <summary>Somewhere sound is played: a device of the system, or a clock where there is none.</summary>
public interface IAudioOutput : IDisposable
{
    AudioFormat Format { get; }

    AudioOutputState State { get; }

    /// <summary>How much sound is kept ready, as the device gave it; zero until started.</summary>
    TimeSpan Buffer { get; }

    /// <summary>What happened since the output was started.</summary>
    AudioOutputStatistics Statistics { get; }

    /// <summary>Plays what <paramref name="renderer"/> makes, until stopped. Returns once the device is open.</summary>
    /// <exception cref="AudioException">The device cannot be opened.</exception>
    void Start(IAudioRenderer renderer);

    /// <summary>Stops, and returns once the renderer is called no more.</summary>
    void Stop();
}
