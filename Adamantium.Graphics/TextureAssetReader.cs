using System.IO;
using System.Threading.Tasks;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Assets;

namespace Adamantium.Graphics;

/// <summary>
/// Loads an image file and uploads it to the GPU as a sampleable <see cref="Texture"/> on the shared
/// resource-loader device, with its mip levels: a scene's material maps (EntityImportTemplate -> Assets.Load&lt;Texture&gt;),
/// seen far off and at a slant.
/// </summary>
public class TextureAssetReader : IAssetReader
{
    public Task<object> ReadAssetAsync(IAssetService assets, AssetReaderParameters parameters)
    {
        var device = (GraphicsDevice)assets.Satellites.Get<IGraphicsDeviceService>().ResourceLoaderDevice;
        if (parameters.Data != null)
        {
            using var stream = new MemoryStream(parameters.Data, false);
            return Task.FromResult((object)Texture.LoadWithMipmaps(device, stream));
        }

        return Task.FromResult((object)Texture.LoadWithMipmaps(device, parameters.AssetPath));
    }
}
