namespace Adamantium.Pulsar;

/// <summary>Where a voice is in space, from its <see cref="Listener"/>, and how it moves. In metres, in the world's axes:
/// its position less the listener's, taken in double and then made float, so that what is near is not lost far from the
/// world's origin.</summary>
public readonly record struct VoicePlace
{
    public Vector3F Position { get; init; }

    /// <summary>In metres a second: what the Doppler shift goes by, with the listener's.</summary>
    public Vector3F Velocity { get; init; }

    /// <summary>Which way it sounds, of any length, for its <see cref="SoundCone"/>; none for every way alike.</summary>
    public Vector3F Facing { get; init; }
}
