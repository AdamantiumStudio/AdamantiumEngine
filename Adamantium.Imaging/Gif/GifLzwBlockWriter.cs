using System.IO;

namespace Adamantium.Imaging.Gif;

/// <summary>LZW codes into GIF data sub-blocks of up to 255 bytes, least significant bit first, the empty block ending
/// them.</summary>
internal sealed class GifLzwBlockWriter(Stream output)
{
    private readonly byte[] _block = new byte[256];
    private int _filled;
    private uint _bits;
    private int _count;

    public void Write(int code, int width)
    {
        _bits |= (uint)code << _count;
        _count += width;
        while (_count >= 8)
        {
            Put((byte)_bits);
            _bits >>= 8;
            _count -= 8;
        }
    }

    public void Finish()
    {
        if (_count > 0)
        {
            Put((byte)_bits);
        }

        Flush();
        output.WriteByte(0);
    }

    private void Put(byte value)
    {
        _block[1 + _filled++] = value;
        if (_filled == 255)
        {
            Flush();
        }
    }

    private void Flush()
    {
        if (_filled == 0)
        {
            return;
        }

        _block[0] = (byte)_filled;
        output.Write(_block, 0, _filled + 1);
        _filled = 0;
    }
}
