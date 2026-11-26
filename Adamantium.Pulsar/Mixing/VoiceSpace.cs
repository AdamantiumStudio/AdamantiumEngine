namespace Adamantium.Pulsar.Mixing;

/// <summary>Where a voice placed in space is, and how it is heard there. Apart from <see cref="Voice"/>: the voices
/// gone through for every block stay small.</summary>
internal struct VoiceSpace
{
    public SpatialSettings Settings;
    public VoicePlace Place;
}
