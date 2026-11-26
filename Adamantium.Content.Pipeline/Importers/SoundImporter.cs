using System.Collections.Generic;
using System.IO;
using Adamantium.Pulsar;

namespace Adamantium.Content.Pipeline.Importers;

/// <summary>Imports a WAV, FLAC, MP3 or Ogg file: the whole of it is decoded to see that it can be, and it is kept as it
/// is.</summary>
[ContentImporter("Sound", ".wav", ".flac", ".mp3", ".ogg", ".oga")]
public sealed class SoundImporter : IContentImporter
{
    /// <summary>Whether the sound is read while it plays: <see cref="WhenLong"/>, <see cref="Always"/> or
    /// <see cref="Never"/>.</summary>
    public const string StreamingSetting = "Streaming";

    /// <summary>A sound longer than <see cref="BakedSound.LongestInMemory"/> is.</summary>
    public const string WhenLong = "Long";

    public const string Always = "Always";

    public const string Never = "Never";

    private static readonly IReadOnlyList<ImportSetting> SoundSettings =
    [
        new(StreamingSetting, ImportSettingKind.Choice, WhenLong, "Read while it plays",
            [new(WhenLong, "Long sounds"), new(Always, "Always"), new(Never, "Never, kept in memory")])
    ];

    public IReadOnlyList<ImportSetting> Settings => SoundSettings;

    public object Import(string sourcePath, ContentBuildContext context)
    {
        var streaming = ImportSettingReader.ReadChoice(sourcePath, context, StreamingSetting, WhenLong, Always, Never);
        var file = File.ReadAllBytes(sourcePath);
        using var decoder = SoundFile.Open(new MemoryStream(file, writable: false));
        var duration = decoder.Format.DurationOf(CountFrames(decoder));
        var isStreamed = streaming == Always || (streaming == WhenLong && duration > BakedSound.LongestInMemory);
        if (isStreamed && decoder.Frames < 0)
        {
            context.Log($"{sourcePath} : warning ACB014: the file does not say how long it is; it is kept in memory");
            isStreamed = false;
        }

        return new ImportedSound(file, isStreamed);
    }

    // All of it is decoded, as nothing else tells whether it can be.
    private static long CountFrames(ISoundDecoder decoder)
    {
        var samples = new float[4096 * decoder.Format.Channels];
        var frames = 0L;
        while (decoder.Read(samples) is var read and not 0)
        {
            frames += read;
        }

        return frames != 0 ? frames : throw new InvalidDataException("The sound file has no samples.");
    }
}
