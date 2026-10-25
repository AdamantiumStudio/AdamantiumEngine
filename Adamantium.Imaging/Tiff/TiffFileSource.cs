using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Adamantium.Imaging.Tiff;

/// <summary>A TIFF file on disk, read at offsets without a shared position - from any number of threads at once.
/// </summary>
internal sealed class TiffFileSource : ITiffSource, IDisposable
{
    private readonly SafeFileHandle _handle;

    public TiffFileSource(string path)
    {
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        Length = RandomAccess.GetLength(_handle);
    }

    public long Length { get; }

    public void Read(long offset, Span<byte> destination)
    {
        ITiffSource.CheckRange(offset, destination.Length, Length);
        while (!destination.IsEmpty)
        {
            var read = RandomAccess.Read(_handle, destination, offset);
            if (read == 0)
            {
                throw new InvalidDataException($"The TIFF file ended at {offset}, short of its length {Length}.");
            }

            destination = destination[read..];
            offset += read;
        }
    }

    public void Dispose() => _handle.Dispose();
}
