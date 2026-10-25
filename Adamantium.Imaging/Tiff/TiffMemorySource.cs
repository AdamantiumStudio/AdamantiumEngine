using System;
using System.IO;

namespace Adamantium.Imaging.Tiff;

/// <summary>A TIFF file held whole in memory.</summary>
internal sealed class TiffMemorySource : ITiffSource
{
    private readonly byte[] _bytes;

    public TiffMemorySource(byte[] bytes)
    {
        _bytes = bytes;
    }

    public long Length => _bytes.Length;

    public void Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > _bytes.Length - destination.Length)
        {
            throw new InvalidDataException(
                $"The TIFF file asks for {destination.Length} bytes at {offset}, past its end at {_bytes.Length}.");
        }

        _bytes.AsSpan((int)offset, destination.Length).CopyTo(destination);
    }
}
