using System.IO;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Pulsar;

namespace Adamantium.ECS.Audio;

/// <summary>The sound in an asset file.</summary>
internal sealed class SoundAssetStorage(string path) : ISoundStorage
{
    public Stream Open() =>
        AssetFile.OpenSection(path, AssetFileSection.Body)
        ?? throw new AssetFormatException($"{path}: it holds no sound; import its source again");
}
