using System.IO;
using System.Threading.Tasks;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Content;

namespace Adamantium.Graphics;

/// <summary>
/// Loads an image file and uploads it to the GPU as a sampleable <see cref="Texture"/> on the shared
/// resource-loader device. Used for model material maps (EntityImportTemplate -> Content.Load&lt;Texture&gt;).
/// </summary>
public class TextureContentReader : IContentReader
{
    public Task<object> ReadContentAsync(IContentManager contentManager, ContentReaderParameters parameters)
    {
        var device = (GraphicsDevice)contentManager.Satellites.Get<IGraphicsDeviceService>().ResourceLoaderDevice;
        if (parameters.Data != null)
        {
            using var stream = new MemoryStream(parameters.Data, false);
            return Task.FromResult((object)Texture.Load(device, stream));
        }

        return Task.FromResult((object)Texture.Load(device, parameters.AssetPath));
    }
}
