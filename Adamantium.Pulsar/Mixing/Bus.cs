namespace Adamantium.Pulsar.Mixing;

/// <summary>What voices and other buses are added into, on the way to its parent.</summary>
internal struct Bus
{
    /// <summary>-1 for the master.</summary>
    public int Parent;

    public Ramp Volume;
    public bool Muted;
    public bool Solo;
    public bool Paused;
    public bool Removing;

    /// <summary>The gain its last block ended with.</summary>
    public float Gain;

    public float[] Samples;
    public bool HasSamples;

    /// <summary>With its parents': how loud what goes into it is in the end.</summary>
    public float Loudness;

    /// <summary>It or a parent is.</summary>
    public bool IsPaused;

    /// <summary>It or a parent is, or none at all.</summary>
    public bool IsSolo;
}
