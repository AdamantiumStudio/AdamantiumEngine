namespace Adamantium.Pulsar.Mixing;

/// <summary>A sound being played.</summary>
internal struct Voice
{
    public SoundData Sound;
    public int Bus;
    public int Priority;
    public bool Loops;
    public bool Paused;
    public bool Stopping;
    public bool Started;
    public bool Ended;

    /// <summary>Whether it is among those heard.</summary>
    public bool Selected;

    public float Pitch;
    public float Pan;
    public Ramp Volume;
    public Ramp Fade;
    public long StartFrame;

    /// <summary>In the sound: frames, and 32 bits of their fraction.</summary>
    public long Position;

    /// <summary>It went round its loop: what is before the loop's start is its end.</summary>
    public bool HasLooped;

    /// <summary>The gains its last block ended with.</summary>
    public float Left;

    public float Right;
}
