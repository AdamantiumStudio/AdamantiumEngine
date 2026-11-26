namespace Adamantium.Pulsar.Tests;

/// <summary>Bits as Vorbis packs them, the lowest of each byte first; bytes of zeros after them, for reading ahead.
/// </summary>
internal sealed class VorbisBitWriter
{
    private readonly List<byte> _bytes = [];
    private int _bits;

    public void Write(uint value, int count)
    {
        for (var bit = 0; bit < count; bit++, _bits++)
        {
            if (_bits % 8 == 0)
            {
                _bytes.Add(0);
            }

            _bytes[^1] |= (byte)((value >> bit & 1) << (_bits % 8));
        }
    }

    public byte[] ToArray() => [.. _bytes, 0, 0, 0, 0, 0, 0, 0, 0];
}
