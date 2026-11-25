namespace Adamantium.Pulsar.Mixing;

/// <summary>What the game asks of the mixer.</summary>
internal struct Command
{
    public CommandKind Kind;

    /// <summary>The slot of the voice or bus.</summary>
    public int Slot;

    /// <summary>The voice's bus, or the bus's parent.</summary>
    public int Bus;

    public float Value;

    public bool Flag;

    /// <summary>How long it takes.</summary>
    public long Frames;

    public SoundData Sound;

    public VoiceSettings Settings;
}
