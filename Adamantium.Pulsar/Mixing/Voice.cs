namespace Adamantium.Pulsar.Mixing;

/// <summary>A sound being played.</summary>
internal struct Voice
{
    /// <summary>What it plays, or null for a <see cref="Stream"/>.</summary>
    public SoundData Sound;

    public SoundStream Stream;
    public int Bus;
    public int Priority;
    public bool Loops;
    public bool Paused;
    public bool Stopping;
    public bool Started;
    public bool Ended;

    /// <summary>Its stream was not read far enough.</summary>
    public bool Starved;

    /// <summary>Whether it is among those heard.</summary>
    public bool Selected;

    /// <summary>What it begins by is there: one answer for a block, or it would begin unheard.</summary>
    public bool IsReady;

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

    public readonly AudioFormat Format => Stream?.Format ?? Sound.Format;

    public readonly float[] Downmix => Stream != null ? Stream.Downmix : Sound.Downmix;
}
