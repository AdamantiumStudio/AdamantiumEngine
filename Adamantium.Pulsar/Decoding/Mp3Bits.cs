using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>Bits of bytes in memory, the highest of each byte first. Past the end it reads zeros, and says so.</summary>
internal ref struct Mp3Bits(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> _bytes = bytes;

    /// <summary>In bits, from the beginning.</summary>
    public int Position { get; set; }

    public readonly int Length => _bytes.Length * 8;

    public readonly bool IsPastEnd => Position > Length;

    /// <summary>The next 32 bits, not read: the first the highest.</summary>
    public readonly uint Peek()
    {
        var at = Position >> 3;
        ulong word;
        if (at + 8 <= _bytes.Length)
        {
            word = BinaryPrimitives.ReadUInt64BigEndian(_bytes[at..]);
        }
        else
        {
            word = 0;
            for (var index = 0; index < 8; index++)
            {
                word = word << 8 | (at + index < _bytes.Length && at + index >= 0 ? _bytes[at + index] : 0u);
            }
        }

        return (uint)(word << (Position & 7) >> 32);
    }

    /// <summary>Up to 24 bits.</summary>
    public int Read(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var value = (int)(Peek() >> (32 - count));
        Position += count;
        return value;
    }

    public int ReadBit() => Read(1);
}
