using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>Reads the bits of packet headers (ITU-T T.800, B.10.1): most significant first, and after a 0xFF byte
/// only seven bits of the next, its top bit being stuffed.</summary>
internal sealed class J2kHeaderBits
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _current;
    private int _left;

    public J2kHeaderBits(byte[] data, int start, int end)
    {
        _data = data;
        Position = start;
        _end = end;
    }

    /// <summary>The next byte the reader has not started.</summary>
    public int Position { get; private set; }

    public int End => _end;

    public byte[] Data => _data;

    public int ReadBit()
    {
        if (_left == 0)
        {
            if (Position >= _end)
            {
                throw new InvalidDataException("A JPEG 2000 packet header ends early.");
            }

            var stuffed = _current == 0xFF;
            _current = _data[Position++];
            _left = stuffed ? 7 : 8;
        }

        _left--;
        return (_current >> _left) & 1;
    }

    public int ReadBits(int count)
    {
        var value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | ReadBit();
        }

        return value;
    }

    /// <summary>Ends a header: the rest of its byte, and the stuffed byte after a final 0xFF.</summary>
    public void Align()
    {
        _left = 0;
        if (_current == 0xFF)
        {
            Position++;
        }

        _current = 0;
    }

    /// <summary>Skips a marker of two bytes when it is next.</summary>
    public void SkipMarker(int marker, int length)
    {
        if (Position + 1 < _end && _data[Position] == marker >> 8 && _data[Position + 1] == (marker & 0xFF))
        {
            Position += length;
        }
    }

    /// <summary>Moves to <paramref name="position"/>, a byte boundary.</summary>
    public void MoveTo(int position)
    {
        Position = position;
        _left = 0;
        _current = 0;
    }
}
