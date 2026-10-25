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
        ITiffSource.CheckRange(offset, destination.Length, Length);
        _bytes.AsSpan((int)offset, destination.Length).CopyTo(destination);
    }
}
