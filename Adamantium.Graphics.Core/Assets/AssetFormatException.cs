using System;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>A file is not an <see cref="AssetFile"/> this engine reads: another format, another version of it, or cut
/// short.</summary>
public class AssetFormatException : Exception
{
    public AssetFormatException() { }

    public AssetFormatException(string message) : base(message) { }

    public AssetFormatException(string message, Exception innerException) : base(message, innerException) { }
}
