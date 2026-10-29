using System;

namespace Adamantium.Graphics.Core.Content;

/// <summary>No resolver of a content manager finds the asset, and it is not embedded.</summary>
public class AssetNotFoundException : Exception
{
    public AssetNotFoundException() { }

    public AssetNotFoundException(string message) : base(message) { }

    public AssetNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
