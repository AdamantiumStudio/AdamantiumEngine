namespace Adamantium.Pulsar;

/// <summary>The frames a looping sound repeats, the one at <paramref name="End"/> left out.</summary>
public readonly record struct SoundLoop(int Start, int End);
