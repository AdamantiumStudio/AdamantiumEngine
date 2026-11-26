namespace Adamantium.Pulsar;

/// <summary>Reads the samples of a sound file, on one thread at a time.</summary>
public interface ISoundDecoder : IDisposable
{
    AudioFormat Format { get; }

    /// <summary>-1 where the file does not say.</summary>
    long Frames { get; }

    /// <summary>What repeats where it is looped; null where the file does not say.</summary>
    SoundLoop? Loop { get; }

    /// <summary>What the samples are coded by, as people name it: "WAV", "FLAC", "MP3", "Vorbis" or "Opus".</summary>
    string Codec { get; }

    /// <summary>Its title, artist and album, where the file says them.</summary>
    SoundTags Tags { get; }

    /// <summary>Fills <paramref name="samples"/>, whole frames, and returns how many frames there were: fewer at the end
    /// alone.</summary>
    /// <exception cref="InvalidDataException">The file is damaged.</exception>
    int Read(Span<float> samples);

    /// <summary>The next frame read is <paramref name="frame"/>.</summary>
    /// <exception cref="InvalidDataException">The file is damaged, or has no such frame.</exception>
    void Seek(long frame);
}
