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

    /// <summary>Throws <see cref="System.IO.InvalidDataException"/> unless <paramref name="count"/> bytes at
    /// <paramref name="offset"/> lie inside a file of <paramref name="length"/>.</summary>
    static void CheckRange(long offset, int count, long length)
    {
        if (offset < 0 || offset > length - count)
        {
            throw new System.IO.InvalidDataException(
                $"The TIFF file asks for {count} bytes at {offset}, past its end at {length}.");
        }
    }
}
