using System.IO;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Pulsar;

namespace Adamantium.Content.Pipeline.Writers;

/// <summary>Writes an <see cref="ImportedSound"/> as the body of a sound's asset file, a <see cref="BakedSound"/>.</summary>
public sealed class SoundWriter : IContentWriter
{
    public string OutputExtension => AssetFile.FileExtension;

    public void Write(Stream stream, object content)
    {
        var sound = (ImportedSound)content;
        BakedSound.Write(stream, sound.File, sound.IsStreamed);
    }
}
