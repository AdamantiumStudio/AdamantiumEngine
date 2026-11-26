namespace Adamantium.Pulsar;

/// <summary>Who hears the voices placed in space: which way their right ear is, and how they move. Where they are is
/// where the voices are placed from (<see cref="VoicePlace.Position"/>).</summary>
public readonly record struct Listener()
{
    /// <summary>Of any length but none: a camera's right. With two speakers, what pans.</summary>
    public Vector3F Right { get; init; } = Vector3F.UnitX;

    /// <summary>In metres a second, in the world's axes: what the Doppler shift goes by, with the voices'.</summary>
    public Vector3F Velocity { get; init; }
}
