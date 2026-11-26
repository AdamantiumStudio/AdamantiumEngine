namespace Adamantium.Pulsar;

/// <summary>A voice of an <see cref="AudioEngine"/>; the default one is none. What is asked of one that ended does nothing.
/// </summary>
public readonly record struct VoiceId
{
    internal VoiceId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    internal int Slot { get; }

    internal int Generation { get; }
}
