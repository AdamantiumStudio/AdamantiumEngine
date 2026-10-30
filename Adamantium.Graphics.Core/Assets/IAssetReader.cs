using System.Threading.Tasks;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>Reads one type of asset from its file or its embedded bytes.</summary>
public interface IAssetReader
{
    /// <summary>The asset <paramref name="parameters"/> name; null when it cannot be read.</summary>
    Task<object> ReadAssetAsync(IAssetService assets, AssetReaderParameters parameters);
}
