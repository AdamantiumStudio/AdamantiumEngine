using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Pulsar;

namespace Adamantium.Content.Pipeline.Importers;

/// <summary>Imports a WAV or FLAC file: the whole of it is decoded to see that it can be, and it is kept as it is.</summary>
[ContentImporter("Sound", ".wav", ".flac")]
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
        var sound = SoundFile.Read(file);
        var isStreamed = streaming == Always || (streaming == WhenLong && sound.Duration > BakedSound.LongestInMemory);
        if (isStreamed && !CanStream(file))
        {
            context.Log($"{sourcePath} : warning ACB012: the file does not say how long it is; it is kept in memory");
            isStreamed = false;
        }

        return new ImportedSound(file, isStreamed);
    }

    private static bool CanStream(byte[] file)
    {
        using var decoder = SoundFile.Open(new MemoryStream(file, writable: false));
        return decoder.Frames > 0;
    }
}
