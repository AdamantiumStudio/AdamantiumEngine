using System.IO;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Content.Pipeline.Writers;

/// <summary>
/// Writes cooked <see cref="SceneData"/> via <see cref="SceneDataSerializer"/> as the body of a baked model (.aemf). The
/// runtime counterpart deserializes it back into <see cref="SceneData"/>.
/// </summary>
public sealed class SceneDataWriter : IContentWriter
{
    public string OutputExtension => AssetFile.FileExtension;

    public void Write(Stream stream, object content)
    {
        SceneDataSerializer.Serialize(stream, (SceneData)content);
    }
}
