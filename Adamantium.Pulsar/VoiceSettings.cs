namespace Adamantium.Pulsar;

/// <summary>How a sound is played.</summary>
public readonly record struct VoiceSettings()
{
    public BusId Bus { get; init; }

    public float Volume { get; init; } = 1;

    /// <summary>How fast it plays: 2 is twice as fast, an octave higher.</summary>
    public float Pitch { get; init; } = 1;

    /// <summary>-1 is left, 1 is right. A sound of one channel keeps its power across, 3 dB down in each at 0; of more, the
    /// other side is turned down.</summary>
    public float Pan { get; init; }

    /// <summary>Of more voices than are heard, those of a higher priority are; among equals the louder.</summary>
    public int Priority { get; init; }

    /// <summary>Whether the sound's <see cref="SoundData.Loop"/> repeats until the voice is stopped.</summary>
    public bool Loops { get; init; }

    /// <summary>Where in the sound it begins.</summary>
    public TimeSpan From { get; init; }

    /// <summary>The <see cref="AudioEngine.Frame"/> it begins at, to the frame; one that passed is now.</summary>
    public long StartFrame { get; init; }

    public TimeSpan FadeIn { get; init; }
}
