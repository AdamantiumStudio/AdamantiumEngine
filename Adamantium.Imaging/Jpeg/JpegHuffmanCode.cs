using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Jpeg;

/// <summary>A Huffman code made for symbol counts (ITU-T T.81, Annex K.2): code lengths by pairing the two rarest,
/// cut to 16 bits, then codes in order of length (C.2).</summary>
internal sealed class JpegHuffmanCode
{
    private readonly byte[] _counts = new byte[16];
    private byte[] _symbols;
    private readonly int[] _codes = new int[256];
    private readonly int[] _sizes = new int[256];

    public JpegHuffmanCode(int[] counts)
    {
        // Counts so skewed that a code passes 32 bits are halved until none does.
        var scaled = (int[])counts.Clone();
        int[] size;
        while (!Lengths(scaled, out size))
        {
            for (var i = 0; i < 256; i++)
            {
                scaled[i] = scaled[i] == 0 ? 0 : (scaled[i] + 1) / 2;
            }
        }

        var bits = new int[33];
        for (var i = 0; i < 257; i++)
        {
            if (size[i] > 0)
            {
                bits[size[i]]++;
            }
        }

        Assign(size, bits);
    }

    // K.2: the code length of each symbol, and of the one kept back; false when one passes 32 bits.
    private static bool Lengths(int[] counts, out int[] size)
    {
        var frequency = new long[257];
        for (var i = 0; i < 256; i++)
        {
            frequency[i] = counts[i];
        }

        // One code point kept back, so no code is all ones.
        frequency[256] = 1;
        size = new int[257];
        var others = new int[257];
        Array.Fill(others, -1);
        while (true)
        {
            int first = -1, second = -1;
            for (var i = 0; i < 257; i++)
            {
                if (frequency[i] > 0 && (first < 0 || frequency[i] <= frequency[first]))
                {
                    first = i;
                }
            }

            for (var i = 0; i < 257; i++)
            {
                if (frequency[i] > 0 && i != first && (second < 0 || frequency[i] <= frequency[second]))
                {
                    second = i;
                }
            }

            if (second < 0)
            {
                break;
            }

            frequency[first] += frequency[second];
            frequency[second] = 0;
            for (size[first]++; others[first] >= 0; size[first]++)
            {
                first = others[first];
            }

            others[first] = second;
            for (size[second]++; others[second] >= 0; size[second]++)
            {
                second = others[second];
            }
        }

        return Array.TrueForAll(size, length => length <= 32);
    }

    private void Assign(int[] size, int[] bits)
    {
        // K.3: lengths past 16 bits folded in, keeping a prefix code.
        for (var i = 32; i > 16; i--)
        {
            while (bits[i] > 0)
            {
                var j = i - 2;
                while (bits[j] == 0)
                {
                    j--;
                }

                bits[i] -= 2;
                bits[i - 1]++;
                bits[j + 1] += 2;
                bits[j]--;
            }
        }

        var longest = 16;
        while (bits[longest] == 0)
        {
            longest--;
        }

        bits[longest]--;
        var symbols = new List<byte>();
        for (var length = 1; length <= 32; length++)
        {
            for (var symbol = 0; symbol < 256; symbol++)
            {
                if (size[symbol] == length)
                {
                    symbols.Add((byte)symbol);
                }
            }
        }

        // The symbols in order of their original lengths take the adjusted lengths in turn.
        var ordered = symbols.ToArray();
        var code = 0;
        var index = 0;
        for (var length = 1; length <= 16; length++)
        {
            _counts[length - 1] = (byte)bits[length];
            for (var i = 0; i < bits[length]; i++, index++, code++)
            {
                _codes[ordered[index]] = code;
                _sizes[ordered[index]] = length;
            }

            code <<= 1;
        }

        _symbols = ordered[..index];
    }

    /// <summary>The DHT entry for this code as table <paramref name="id"/> (class in the high nibble).</summary>
    public byte[] Table(byte id) => [id, .. _counts, .. _symbols];

    public void Write(JpegBitWriter bits, int symbol) => bits.Write(_codes[symbol], _sizes[symbol]);
}
