namespace Adamantium.Content.Pipeline;

/// <summary>A sound a source was imported into: its file, checked to be one, and whether it is read while it plays.</summary>
public sealed class ImportedSound
{
    public ImportedSound(byte[] file, bool isStreamed)
    {
        File = file;
        IsStreamed = isStreamed;
    }

    public byte[] File { get; }

    public bool IsStreamed { get; }
}
