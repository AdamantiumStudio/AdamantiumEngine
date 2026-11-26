using Adamantium.Core.Compression;

namespace Adamantium.Pulsar.Decoding;

/// <summary>A Vorbis codebook (Vorbis I, 3): entries of Huffman codewords, each, where it has a lookup, standing for a
/// vector of its dimensions.</summary>
internal sealed class VorbisCodebook
{
    // Of entries times dimensions: more is no codebook an encoder makes.
    private const int MostValues = 1 << 24;

    private readonly PrefixCode _code;
    // The one entry of a codebook of one, read as one bit (errata 20150226); -1 for any other.
    private readonly int _only;
    private readonly float[] _values;

    private VorbisCodebook(int dimensions, int entries, PrefixCode code, int only, float[] values)
    {
        Dimensions = dimensions;
        Entries = entries;
        _code = code;
        _only = only;
        _values = values;
    }

    public int Dimensions { get; }

    public int Entries { get; }

    /// <summary>Whether it has a lookup, and so can be read as vectors.</summary>
    public bool HasValues => _values != null;

    /// <exception cref="InvalidDataException">It is no codebook the specification allows.</exception>
    public static VorbisCodebook Read(ref VorbisBits bits)
    {
        if (bits.Read(24) != 0x564342)
        {
            throw Damaged("a codebook does not begin as one");
        }

        var dimensions = (int)bits.Read(16);
        var entries = (int)bits.Read(24);
        var lengths = new int[entries];
        if (bits.ReadFlag())
        {
            // Ordered: how many there are of each length, from the shortest.
            var length = (int)bits.Read(5) + 1;
            for (var entry = 0; entry < entries; length++)
            {
                var count = (int)bits.Read(ILog(entries - entry));
                if (count > entries - entry || length > PrefixCode.LongestCode || bits.IsEnded)
                {
                    throw Damaged("a codebook has more entries of its lengths than it has");
                }

                lengths.AsSpan(entry, count).Fill(length);
                entry += count;
            }
        }
        else
        {
            var isSparse = bits.ReadFlag();
            for (var entry = 0; entry < entries; entry++)
            {
                lengths[entry] = !isSparse || bits.ReadFlag() ? (int)bits.Read(5) + 1 : 0;
            }
        }

        var lookup = (int)bits.Read(4);
        float[] values = null;
        if (lookup is 1 or 2)
        {
            values = ReadValues(ref bits, lookup, dimensions, entries);
        }
        else if (lookup != 0)
        {
            throw Damaged($"a codebook has a lookup of type {lookup}");
        }

        if (bits.IsEnded)
        {
            throw Damaged("the setup ends within a codebook");
        }

        var used = 0;
        var only = -1;
        for (var entry = 0; entry < entries; entry++)
        {
            if (lengths[entry] > 0)
            {
                used++;
                only = entry;
            }
        }

        // A codebook of one entry has a codeword of one bit, read whatever it is.
        if (used == 1)
        {
            return lengths[only] == 1
                ? new VorbisCodebook(dimensions, entries, default, only, values)
                : throw Damaged("a codebook of one entry has a codeword longer than a bit");
        }

        if (!TryAssign(lengths, out var words) || !PrefixCode.TryCreate(words, PrefixBitOrder.LowFirst, out var code) ||
            used > 0 && !code.IsComplete)
        {
            throw Damaged("a codebook's codeword lengths make no whole code");
        }

        return new VorbisCodebook(dimensions, entries, code, -1, values);
    }

    /// <summary>The next entry; -1 where the packet ends within it.</summary>
    public int ReadEntry(ref VorbisBits bits)
    {
        if (_only >= 0)
        {
            bits.Skip(1);
            return bits.IsEnded ? -1 : _only;
        }

        var entry = _code.Decode(bits.Peek(), out var length);
        bits.Skip(length);
        return bits.IsEnded ? -1 : entry;
    }

    /// <summary>The vector of the next entry; empty where the packet ends within it.</summary>
    public ReadOnlySpan<float> ReadVector(ref VorbisBits bits)
    {
        var entry = ReadEntry(ref bits);
        return entry < 0 ? default : _values.AsSpan(entry * Dimensions, Dimensions);
    }

    /// <summary>ilog of 9.2.1: the number of the highest bit set, 0 for none.</summary>
    public static int ILog(long value) => value <= 0 ? 0 : 64 - (int)long.LeadingZeroCount(value);

    /// <summary>float32_unpack of 9.2.2.</summary>
    public static float Unpack(uint packed)
    {
        double mantissa = packed & 0x1FFFFF;
        var exponent = (int)((packed & 0x7FE00000) >> 21);
        return (float)Math.ScaleB((packed & 0x80000000) != 0 ? -mantissa : mantissa, exponent - 788);
    }

    // The vectors of 3.2.1, unpacked for every entry: of lookup 1 a lattice of values each dimension shares, of 2 each
    // value its own.
    private static float[] ReadValues(ref VorbisBits bits, int lookup, int dimensions, int entries)
    {
        var minimum = Unpack(bits.Read(32));
        var delta = Unpack(bits.Read(32));
        var valueBits = (int)bits.Read(4) + 1;
        var isSequence = bits.ReadFlag();
        if ((long)entries * dimensions > MostValues)
        {
            throw Damaged("a codebook has more values than any encoder writes");
        }

        var count = lookup == 1 ? Lookup1Values(entries, dimensions) : entries * dimensions;
        var multiplicands = new int[count];
        for (var index = 0; index < count; index++)
        {
            multiplicands[index] = (int)bits.Read(valueBits);
        }

        var values = new float[entries * dimensions];
        for (var entry = 0; entry < entries; entry++)
        {
            var last = 0f;
            var divisor = 1L;
            for (var dimension = 0; dimension < dimensions; dimension++)
            {
                var offset = lookup == 1 ? (int)(entry / divisor % count) : entry * dimensions + dimension;
                var value = multiplicands[offset] * delta + minimum + last;
                values[entry * dimensions + dimension] = value;
                if (isSequence)
                {
                    last = value;
                }

                divisor = Math.Min(divisor * count, long.MaxValue / Math.Max(1, count));
            }
        }

        return values;
    }

    // lookup1_values of 9.2.3: the greatest whose power of the dimensions is no more than the entries.
    private static int Lookup1Values(int entries, int dimensions)
    {
        if (dimensions == 0)
        {
            throw Damaged("a codebook of a lattice has no dimensions");
        }

        var value = (int)Math.Floor(Math.Pow(entries, 1.0 / dimensions));
        while (Math.Pow(value + 1, dimensions) <= entries)
        {
            value++;
        }

        while (value > 0 && Math.Pow(value, dimensions) > entries)
        {
            value--;
        }

        return Math.Max(value, 1);
    }

    // 3.2.1: each used entry, in order, the lowest codeword of its length no other is, or begins. The free parts of the
    // tree are kept as prefixes, in the order of their bits: the lowest that can hold a codeword of the length gives
    // it, and what is left of it beside the codeword's path is free.
    private static bool TryAssign(int[] lengths, out PrefixCodeWord[] words)
    {
        var assigned = new List<PrefixCodeWord>();
        var free = new List<(ulong Start, int Depth)> { (0, 0) };
        for (var entry = 0; entry < lengths.Length; entry++)
        {
            var length = lengths[entry];
            if (length == 0)
            {
                continue;
            }

            var at = free.FindIndex(node => node.Depth <= length);
            if (at < 0)
            {
                words = null;
                return false;
            }

            var (start, depth) = free[at];
            free.RemoveAt(at);
            // The left path down to the length: the right sibling at each level is left free, the deepest, which
            // begins first, put in last, before the others.
            var code = start >> (PrefixCode.LongestCode - length);
            for (var level = depth + 1; level <= length; level++)
            {
                var sibling = (code >> (length - level) ^ 1) << (PrefixCode.LongestCode - level);
                free.Insert(at, (sibling, level));
            }

            assigned.Add(new PrefixCodeWord((uint)code, length, entry));
        }

        words = [.. assigned];
        return true;
    }

    private static InvalidDataException Damaged(string how) => new($"The Vorbis stream is damaged: {how}.");
}
