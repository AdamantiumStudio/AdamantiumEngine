namespace Adamantium.Pulsar;

/// <summary>How a voice is placed in space: heard from where it is, from the <see cref="Listener"/>. It is mixed to one
/// channel and panned by its place; its own pan is not heard.</summary>
public readonly record struct SpatialSettings()
{
    /// <summary>Where it begins; <see cref="AudioEngine.SetPlace"/> moves it.</summary>
    public VoicePlace Place { get; init; }

    public Attenuation Attenuation { get; init; } = new();

    public SoundCone Cone { get; init; } = new();

    /// <summary>How much of the Doppler shift is heard: 0 none, 1 as it is.</summary>
    public float DopplerScale { get; init; } = 1;

    internal void Check()
    {
        Attenuation.Check();
        Cone.Check();
        AudioEngine.CheckPlace(Place);
        if (!(DopplerScale >= 0 && float.IsFinite(DopplerScale)))
        {
            throw new ArgumentOutOfRangeException(nameof(DopplerScale), DopplerScale, "A Doppler scale is 0 or more.");
        }
    }
}
