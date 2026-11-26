namespace Adamantium.Pulsar;

/// <summary>Who hears the voices placed in space: which way they face, and how they move. Where they are is where the
/// voices are placed from (<see cref="VoicePlace.Position"/>).</summary>
public readonly record struct Listener()
{
    /// <summary>Which way they look, of any length: a camera's forward.</summary>
    public Vector3F Forward { get; init; } = Vector3F.UnitZ;

    /// <summary>Which way their right ear is, of any length: a camera's right. With two speakers, what pans.</summary>
    public Vector3F Right { get; init; } = Vector3F.UnitX;

    /// <summary>In metres a second, in the world's axes: what the Doppler shift goes by, with the voices'.</summary>
    public Vector3F Velocity { get; init; }
}
