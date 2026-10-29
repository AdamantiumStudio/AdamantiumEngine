using System.Threading.Tasks;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Imaging;

namespace Adamantium.Graphics;

public class ImageAssetReader : IAssetReader
{
    public Task<object> ReadAssetAsync(IAssetService assets, AssetReaderParameters parameters)
    {
        var image = Image.Load(parameters.AssetPath);
        if (image != null)
        {
            image.Name = parameters.AssetName;
        }

        return Task.FromResult((object)image);
    }
}
