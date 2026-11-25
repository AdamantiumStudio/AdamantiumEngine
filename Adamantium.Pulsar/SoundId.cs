namespace Adamantium.Pulsar;

/// <summary>A sound given to an <see cref="AudioEngine"/>; the default one is none.</summary>
public readonly record struct SoundId
{
    internal SoundId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    internal int Slot { get; }

    internal int Generation { get; }
}
