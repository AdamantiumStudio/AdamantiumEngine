using System.IO;

namespace Adamantium.Content.Pipeline;

/// <summary>
/// Serializes a cooked intermediate to the baked binary form. The runtime counterpart is
/// <c>Adamantium.Graphics.Core.Assets.IAssetReader</c>.
/// </summary>
public interface IContentWriter
{
    /// <summary>Extension of the produced artifact, e.g. <c>.aemf</c>.</summary>
    string OutputExtension { get; }

    void Write(Stream stream, object content);
}
