namespace Adamantium.Pulsar;

/// <summary>A bus of an <see cref="AudioEngine"/>; the default one is the master, which all others lead to.</summary>
public readonly record struct BusId
{
    internal BusId(int slot, int generation)
    {
        Slot = slot;
        Generation = generation;
    }

    public static BusId Master => default;

    internal int Slot { get; }

    internal int Generation { get; }
}
