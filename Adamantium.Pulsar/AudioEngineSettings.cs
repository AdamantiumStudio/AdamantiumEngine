namespace Adamantium.Pulsar;

public sealed class AudioEngineSettings
{
    /// <summary>How many voices there can be, heard or not.</summary>
    public int Voices { get; init; } = 1024;

    /// <summary>How many of them are heard at a time. The rest go on unheard, and are heard from where they got to.</summary>
    public int AudibleVoices { get; init; } = 64;

    /// <summary>How many buses there can be, the master among them.</summary>
    public int Buses { get; init; } = 64;
}
