using System;
using System.IO;
using System.Threading;

namespace Adamantium.Imaging.Tiff;

/// <summary>A TIFF file in a seekable stream; reads take turns, as a stream has one position.</summary>
internal sealed class TiffStreamSource : ITiffSource, IDisposable
{
    private readonly Lock _reading = new();
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public TiffStreamSource(Stream stream, bool leaveOpen)
    {
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("A TIFF file is read from a stream that can seek.", nameof(stream));
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
        Length = stream.Length;
    }

    public long Length { get; }

    public void Read(long offset, Span<byte> destination)
    {
        ITiffSource.CheckRange(offset, destination.Length, Length);
        lock (_reading)
        {
            _stream.Position = offset;
            try
            {
                _stream.ReadExactly(destination);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException($"The TIFF stream ended short of its length {Length}.", exception);
            }
        }
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
