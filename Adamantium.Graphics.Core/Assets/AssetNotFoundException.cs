using System;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>No resolver of an asset service finds the asset, and it is not embedded.</summary>
public class AssetNotFoundException : Exception
{
    public AssetNotFoundException() { }

    public AssetNotFoundException(string message) : base(message) { }

    public AssetNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
