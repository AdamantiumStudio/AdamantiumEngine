using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Core.Compression;

namespace Adamantium.Imaging.Jpeg;

/// <summary>One Huffman table of a JPEG stream (ITU-T T.81, Annex C): its codes assigned as C.2 does, decoded by a
/// <see cref="PrefixCode"/>; and, JPEG's own, a look-up of a code with the value its symbol says follows.</summary>
internal sealed class JpegHuffman
{
    /// <summary>Bits a code is looked up with its value by at once.</summary>
    public const int FastBits = 9;

    private readonly PrefixCode _code;
    private readonly int[] _withValue = new int[1 << FastBits];

    /// <summary>The table of <paramref name="counts"/> - how many codes of each length 1 to 16 - and their
    /// <paramref name="symbols"/> in code order. Counts that do not make a prefix code throw
    /// <see cref="InvalidDataException"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public JpegHuffman(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> symbols)
    {
        var total = 0;
        foreach (var count in counts[..16])
        {
            total += count;
        }

        // The lengths in code order, so that the canonical codes are those of C.2 in order too.
        var lengths = new int[Math.Min(total, symbols.Length)];
        var at = 0;
        for (var length = 1; length <= 16; length++)
        {
            for (var one = 0; one < counts[length - 1] && at < lengths.Length; one++)
            {
                lengths[at++] = length;
            }
        }

        if (!PrefixCode.TryCanonical(lengths, out var words))
        {
            throw new InvalidDataException("A JPEG Huffman table has more codes than its lengths allow.");
        }

        for (var index = 0; index < words.Length; index++)
        {
            words[index] = words[index] with { Symbol = symbols[words[index].Symbol] };
        }

        PrefixCode.TryCreate(words, PrefixBitOrder.HighFirst, out _code);
        for (var bits = 0; bits < _withValue.Length; bits++)
        {
            var symbol = _code.Decode((ulong)bits << (64 - FastBits), out var length);
            var size = symbol & 15;
            if (length == 0 || length + size > FastBits)
            {
                continue;
            }

            var raw = (bits >> (FastBits - length - size)) & ((1 << size) - 1);
            var value = size == 0 ? 0 : raw < 1 << (size - 1) ? raw - (1 << size) + 1 : raw;
            _withValue[bits] = (value << 16) | (symbol << 8) | (length + size);
        }
    }

    /// <summary>The code at the top of <paramref name="bits"/> together with the value its symbol's low four bits say
    /// follow, when both fit <see cref="FastBits"/>: the bits they take in the low byte, the symbol in the next, the
    /// value signed in the high half. 0 when they do not fit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeWithValue(ulong bits) =>
        Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_withValue), (nint)(bits >> (64 - FastBits)));

    /// <summary>The symbol of the code at the top of <paramref name="bits"/> (the next bits, most significant first),
    /// and its length; a length of 0 when no code matches - broken data.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ulong bits, out int length) => _code.Decode(bits, out length);
}
