using System.IO;
using System.Threading.Tasks;
using Adamantium.DarkMatter;
using Adamantium.Graphics.Core.Assets;

namespace Adamantium.ECS.Physics;

/// <summary>Reads a physics material file (<see cref="PhysicsMaterialFile"/>) as a <see cref="PhysicsMaterial"/>.</summary>
public sealed class PhysicsMaterialAssetReader : IAssetReader
{
    public Task<object> ReadAssetAsync(IAssetService assets, AssetReaderParameters parameters)
    {
        using Stream stream = parameters.Data != null
            ? new MemoryStream(parameters.Data, false)
            : File.OpenRead(parameters.AssetPath);
        return Task.FromResult((object)PhysicsMaterialFile.Read(stream).ToMaterial());
    }
}
