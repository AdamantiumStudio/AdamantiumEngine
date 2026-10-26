using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Jpeg;

/// <summary>One Huffman table of a JPEG stream (ITU-T T.81, Annex C), built for decoding (F.2.2.3): codes of up to
/// <see cref="FastBits"/> bits are found in one look-up, longer ones by the largest code of each length.</summary>
internal sealed class JpegHuffman
{
    /// <summary>Bits a code is looked up by at once.</summary>
    public const int FastBits = 9;

    // Per look-up value: the code's length in the high byte and its symbol in the low one; 0 for a longer code.
    private readonly ushort[] _fast = new ushort[1 << FastBits];
    private readonly int[] _largest = new int[18];
    private readonly int[] _offset = new int[17];
    private readonly byte[] _symbols;

    /// <summary>The table of <paramref name="counts"/> - how many codes of each length 1 to 16 - and their
    /// <paramref name="symbols"/> in code order. Counts that do not make a prefix code throw
    /// <see cref="InvalidDataException"/>.</summary>
    public JpegHuffman(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
    {
        _symbols = symbols.ToArray();
        var code = 0;
        var index = 0;
        for (var length = 1; length <= 16; length++)
        {
            var count = counts[length - 1];
            _offset[length] = index - code;
            if (code + count > 1 << length)
            {
                throw new InvalidDataException("A JPEG Huffman table has more codes than its lengths allow.");
            }

            if (count > 0)
            {
                for (var i = 0; i < count; i++, code++, index++)
                {
                    if (length <= FastBits)
                    {
                        var shift = FastBits - length;
                        var entry = (ushort)((length << 8) | _symbols[index]);
                        _fast.AsSpan(code << shift, 1 << shift).Fill(entry);
                    }
                }

                _largest[length] = code - 1;
            }
            else
            {
                _largest[length] = -1;
            }

            code <<= 1;
        }

        _largest[17] = int.MaxValue;
    }

    /// <summary>The symbol of the code at the top of <paramref name="bits"/> (the next bits, most significant first),
    /// and its length; a length of 0 when no code matches - broken data.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ulong bits, out int length)
    {
        var entry = _fast[(int)(bits >> (64 - FastBits))];
        if (entry != 0)
        {
            length = entry >> 8;
            return entry & 0xFF;
        }

        return Long(bits, out length);
    }

    private int Long(ulong bits, out int length)
    {
        for (length = FastBits + 1; length <= 16; length++)
        {
            var code = (int)(bits >> (64 - length));
            if (code <= _largest[length])
            {
                return _symbols[code + _offset[length]];
            }
        }

        length = 0;
        return 0;
    }
}
