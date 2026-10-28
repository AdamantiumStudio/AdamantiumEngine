using System;

namespace Adamantium.Graphics.Core.Content;

/// <summary>What a reader is given to read an asset.</summary>
public struct ContentReaderParameters
{
    /// <summary>The name the asset was loaded by.</summary>
    public String AssetName;

    /// <summary>The file a resolver found it in; its name when it is <see cref="Data"/>.</summary>
    public String AssetPath;

    /// <summary>The bytes of an <see cref="IContentManager.Embed">embedded</see> asset; null for a file.</summary>
    public byte[] Data;

    /// <summary>The type it is loaded as.</summary>
    public Type AssetType;

    /// <summary>The options the load was given.</summary>
    public object Options;
}
