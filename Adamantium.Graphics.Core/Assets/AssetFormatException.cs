using System;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>A file is not an <see cref="AssetFile"/> this engine reads: another format, another version of it, or cut
/// short.</summary>
public class AssetFormatException : Exception
{
    public AssetFormatException() { }

    public AssetFormatException(string message) : base(message) { }

    public AssetFormatException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>A file written in a format version this engine does not read.</summary>
    public AssetFormatException(string message, ushort writtenVersion) : base(message)
    {
        WrittenVersion = writtenVersion;
    }

    /// <summary>A file refused by its path, for the same reason as <paramref name="innerException"/>.</summary>
    public AssetFormatException(string message, AssetFormatException innerException) : base(message, innerException)
    {
        WrittenVersion = innerException.WrittenVersion;
    }

    /// <summary>The format version the file was written in when that is why it is refused; null for any other fault.
    /// </summary>
    public ushort? WrittenVersion { get; }
}
