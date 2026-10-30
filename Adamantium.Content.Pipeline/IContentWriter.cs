using System.IO;

namespace Adamantium.Content.Pipeline;

/// <summary>
/// Serializes a cooked intermediate to the body of an <see cref="Adamantium.Graphics.Core.Assets.AssetFile"/>. The
/// runtime counterpart is <see cref="Adamantium.Graphics.Core.Assets.IAssetReader"/>.
/// </summary>
public interface IContentWriter
{
    /// <summary>Extension of the produced artifact, e.g. <c>.aemf</c>.</summary>
    string OutputExtension { get; }

    void Write(Stream stream, object content);
}
