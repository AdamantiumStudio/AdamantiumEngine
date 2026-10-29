using System.Threading.Tasks;

namespace Adamantium.Graphics.Core.Content;

/// <summary>Reads one type of asset from its file or its embedded bytes.</summary>
public interface IContentReader
{
    /// <summary>The asset <paramref name="parameters"/> name; null when it cannot be read.</summary>
    Task<object> ReadContentAsync(IContentManager contentManager, ContentReaderParameters parameters);
}
