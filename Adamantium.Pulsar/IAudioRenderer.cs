namespace Adamantium.Pulsar;

/// <summary>Makes the sound an <see cref="IAudioOutput"/> plays.</summary>
public interface IAudioRenderer
{
    /// <summary>Fills <paramref name="samples"/>, whole frames of <paramref name="format"/>. Called on the output's own
    /// thread, which must not wait or allocate here: a late answer is heard as a gap.</summary>
    void Render(Span<float> samples, AudioFormat format);
}
