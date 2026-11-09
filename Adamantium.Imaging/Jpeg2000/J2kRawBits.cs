namespace Adamantium.Imaging.Jpeg2000;

/// <summary>Reads the raw bits of a bypassed coding pass (ITU-T T.800, D.6): after a 0xFF byte the next gives seven.
/// Past the end it reads ones.</summary>
internal sealed class J2kRawBits
{
    private readonly byte[] _data;
    private int _position;
    private int _current;
    private int _left;

    public J2kRawBits(byte[] data)
    {
        _data = data;
    }

    public int ReadBit()
    {
        if (_left == 0)
        {
            var stuffed = _current == 0xFF;
            _current = _position < _data.Length ? _data[_position] : 0xFF;
            _position++;
            _left = stuffed ? 7 : 8;
        }

        _left--;
        return (_current >> _left) & 1;
    }
}
