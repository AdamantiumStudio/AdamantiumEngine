using System.IO;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Pulsar;

namespace Adamantium.ECS.Audio;

/// <summary>The sound in an asset file.</summary>
internal sealed class SoundAssetStorage(string path) : ISoundStorage
{
    // Where it is, may be another place after an import.
    public Stream Open() =>
        AssetFile.TryFindSection(path, AssetFileSection.Body, out var offset, out var length)
            ? new SoundFileStorage(path, offset, length).Open()
            : throw new AssetFormatException($"{path}: it holds no sound; import its source again");
}
