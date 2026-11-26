using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>The bits of a Vorbis packet, the lowest of each byte first (Vorbis I, 2). A read past the end is the
/// packet's end, no error: it gives zeros, and says so from then on.</summary>
internal ref struct VorbisBits(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> _bytes = bytes;

    /// <summary>In bits, from the beginning.</summary>
    public int Position { get; private set; }

    /// <summary>A read went past the end.</summary>
    public bool IsEnded { get; private set; }

    public readonly int Length => _bytes.Length * 8;

    /// <summary>Up to 32 bits, the first read the lowest.</summary>
    public uint Read(int count)
    {
        // No bits: 0, ended or not (2.1.9).
        if (count == 0)
        {
            return 0;
        }

        if (IsEnded || Position + count > Length)
        {
            IsEnded = true;
            Position = Length;
            return 0;
        }

        var value = (uint)(Peek() & (count == 32 ? uint.MaxValue : (1UL << count) - 1));
        Position += count;
        return value;
    }

    public bool ReadFlag() => Read(1) != 0;

    /// <summary>The next 64 bits, not read, the next the lowest; zeros past the end.</summary>
    public readonly ulong Peek()
    {
        var at = Position >> 3;
        ulong bits;
        if (at + 8 <= _bytes.Length)
        {
            bits = BinaryPrimitives.ReadUInt64LittleEndian(_bytes[at..]);
        }
        else
        {
            bits = 0;
            for (var index = 0; index < 8 && at + index < _bytes.Length; index++)
            {
                bits |= (ulong)_bytes[at + index] << (8 * index);
            }
        }

        var shift = Position & 7;
        return shift == 0 || at + 8 >= _bytes.Length ? bits >> shift : bits >> shift | (ulong)_bytes[at + 8] << (64 - shift);
    }

    /// <summary>Passes <paramref name="count"/> bits; past the end, it ends.</summary>
    public void Skip(int count)
    {
        if (Position + count > Length)
        {
            IsEnded = true;
            Position = Length;
            return;
        }

        Position += count;
    }
}
