namespace Adamantium.Pulsar;

/// <summary>Where a <see cref="StreamedSound"/> is read from.</summary>
public interface ISoundStorage
{
    /// <summary>The sound file anew, from its beginning: each voice reads one of its own. The stream seeks. Called on a
    /// thread of the engine's.</summary>
    Stream Open();
}
