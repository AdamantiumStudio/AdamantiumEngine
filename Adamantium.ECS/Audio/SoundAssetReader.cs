using System;
using System.IO;
using System.Threading.Tasks;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Pulsar;

namespace Adamantium.ECS.Audio;

/// <summary>Reads a sound: an asset file as it was imported, a sound file as it is - streamed where it is long.</summary>
public sealed class SoundAssetReader : IAssetReader
{
    public Task<object> ReadAssetAsync(IAssetService assets, AssetReaderParameters parameters) =>
        Task.FromResult((object)Read(parameters));

    private static Sound Read(in AssetReaderParameters parameters)
    {
        if (parameters.Data != null)
        {
            return ReadFile(new SoundMemoryStorage(parameters.Data));
        }

        return parameters.AssetPath.EndsWith(AssetFile.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? BakedSound.Read(new SoundAssetStorage(parameters.AssetPath))
            : ReadFile(new SoundFileStorage(parameters.AssetPath));
    }

    private static Sound ReadFile(ISoundStorage storage)
    {
        using (var decoder = SoundFile.Open(storage.Open()))
        {
            if (decoder.Frames > 0 && decoder.Format.DurationOf(decoder.Frames) > BakedSound.LongestInMemory)
            {
                return StreamedSound.Open(storage);
            }
        }

        return SoundFile.Read(storage.Open());
    }
}
