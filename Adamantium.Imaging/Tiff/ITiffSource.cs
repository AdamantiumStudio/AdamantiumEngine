using System;

namespace Adamantium.Imaging.Tiff;

/// <summary>Bytes of a TIFF file read at any offset - a buffer in memory or a file - from any thread.</summary>
internal interface ITiffSource
{
    /// <summary>The file's length in bytes.</summary>
    long Length { get; }

    /// <summary>Fills <paramref name="destination"/> from <paramref name="offset"/>; throws
    /// <see cref="System.IO.InvalidDataException"/> when that runs past the end.</summary>
    void Read(long offset, Span<byte> destination);
}
