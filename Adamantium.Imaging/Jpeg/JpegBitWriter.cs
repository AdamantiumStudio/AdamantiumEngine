using System.IO;

namespace Adamantium.Imaging.Jpeg;

/// <summary>Bits into a JPEG entropy-coded segment, most significant first, 0xFF stuffed with 0x00 (F.1.2.3);
/// the last byte padded with ones.</summary>
internal sealed class JpegBitWriter
{
    private readonly Stream _output;
    private readonly byte[] _buffer = new byte[1 << 16];
    private int _used;
    private ulong _bits;
    private int _count;

    public JpegBitWriter(Stream output)
    {
        _output = output;
    }

    public void Write(int value, int size)
    {
        if (size == 0)
        {
            return;
        }

        _bits = (_bits << size) | ((uint)value & ((1u << size) - 1));
        _count += size;
        while (_count >= 8)
        {
            _count -= 8;
            Put((byte)(_bits >> _count));
        }
    }

    public void Flush()
    {
        if (_count > 0)
        {
            Write((1 << (8 - _count)) - 1, 8 - _count);
        }

        _output.Write(_buffer, 0, _used);
        _used = 0;
    }

    private void Put(byte value)
    {
        if (_used + 2 > _buffer.Length)
        {
            _output.Write(_buffer, 0, _used);
            _used = 0;
        }

        _buffer[_used++] = value;
        if (value == 0xFF)
        {
            _buffer[_used++] = 0;
        }
    }
}
