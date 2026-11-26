using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Core.Compression;

/// <summary>A prefix code - Huffman's, as JPEG, JBIG2, MP3, Vorbis and PRC store theirs - decoded from the bits that
/// come next: a look-up of up to 9 of them, and, for a longer code, one in a table of its own for each further 9. How
/// a format assigns its codes is its own; <see cref="TryCanonical"/> is the way most do. A value, kept within its
/// holder: decoding reads its table with no object between. One left default is no code, and is not decoded by.
/// </summary>
public readonly struct PrefixCode
{
    /// <summary>The longest codeword.</summary>
    public const int LongestCode = 32;

    private const int TableBits = 9;
    private const int LengthBits = 6;
    private const int TableBitsBits = 4;
    private const int MostSymbols = 1 << 24;

    // A value and its codeword's whole length, of 1 or more: symbol << 6 | length. Another table: -1 - (its start << 4
    // | the bits it is looked up by). 0: no codeword begins so.
    private readonly int[] _entries;
    private readonly int _firstBits;
    // Where the first table's index is in the bits, whichever their order.
    private readonly int _firstShift;
    private readonly int _firstMask;
    // The symbol of a code of one codeword of no bits; -1 for any other.
    private readonly int _onlySymbol;

    private PrefixCode(int[] entries, int firstBits, PrefixBitOrder order, int shortest, bool isComplete, int onlySymbol)
    {
        _entries = entries;
        _firstBits = firstBits;
        _firstShift = order == PrefixBitOrder.HighFirst ? 64 - firstBits : 0;
        _firstMask = (1 << firstBits) - 1;
        _onlySymbol = onlySymbol;
        Order = order;
        Shortest = shortest;
        IsComplete = isComplete;
    }

    public PrefixBitOrder Order { get; }

    /// <summary>The shortest codeword's length; <see cref="LongestCode"/> for none.</summary>
    public int Shortest { get; }

    /// <summary>Whether every run of bits begins with a codeword.</summary>
    public bool IsComplete { get; }

    /// <summary>Codes assigned the way of JPEG, JBIG2 and Deflate: shorter first, those of one length in their order,
    /// each the one after the last, made one longer at each longer length. Symbol i has <paramref name="lengths"/>[i]
    /// bits; 0 is no codeword. The words are in the symbols' order. False where the lengths are more than bits of them
    /// can tell apart.</summary>
    public static bool TryCanonical(ReadOnlySpan<int> lengths, out PrefixCodeWord[] words)
    {
        words = null;
        Span<int> counts = stackalloc int[LongestCode + 1];
        var count = 0;
        foreach (var length in lengths)
        {
            if (length is < 0 or > LongestCode)
            {
                return false;
            }

            counts[length]++;
            count += length > 0 ? 1 : 0;
        }

        Span<long> next = stackalloc long[LongestCode + 1];
        long code = 0;
        for (var length = 1; length <= LongestCode; length++)
        {
            code = (code + (length > 1 ? counts[length - 1] : 0)) << 1;
            next[length] = code;
            if (code + counts[length] > 1L << length)
            {
                return false;
            }
        }

        words = new PrefixCodeWord[count];
        var placed = 0;
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol];
            if (length > 0)
            {
                words[placed++] = new PrefixCodeWord((uint)next[length]++, length, symbol);
            }
        }

        return true;
    }

    /// <summary>The code of <paramref name="words"/>; false where one is a prefix of another, or one is not of 0 to
    /// <see cref="LongestCode"/> bits or of a symbol from 0 to 2^24. A single codeword of no bits is a code of one
    /// symbol, read in no bits.</summary>
    public static bool TryCreate(ReadOnlySpan<PrefixCodeWord> words, PrefixBitOrder order, out PrefixCode code)
    {
        code = default;
        ulong space = 0;
        var shortest = LongestCode;
        foreach (var word in words)
        {
            if (word.Length is < 0 or > LongestCode || word.Symbol is < 0 or >= MostSymbols ||
                word.Length < 32 && word.Code >> word.Length != 0 || word.Length == 0 && words.Length != 1)
            {
                return false;
            }

            // Of 2^32, the part of all bit runs that begin with it.
            space += 1UL << (LongestCode - word.Length);
            shortest = Math.Min(shortest, word.Length);
        }

        if (space > 1UL << LongestCode)
        {
            return false;
        }

        if (shortest == 0)
        {
            code = new PrefixCode(new int[2], 1, order, 0, true, words[0].Symbol);
            return true;
        }

        // In the order of their bits, left aligned: the words of one table, sharing the bits before it, are a run.
        var sorted = new int[words.Length];
        var keys = new ulong[words.Length];
        for (var index = 0; index < sorted.Length; index++)
        {
            sorted[index] = index;
            keys[index] = (ulong)words[index].Code << (LongestCode - words[index].Length);
        }

        Array.Sort(keys, sorted);
        var entries = new Entries();
        // Each table: its run of words, the bits before it, and where in the table above it is linked from. No more
        // than four deep, as 32 bits are of 9 at a time.
        var pending = new Stack<Pending>();
        pending.Push(new Pending(0, sorted.Length, 0, -1));
        var firstBits = 0;
        while (pending.TryPop(out var table))
        {
            if (!Fill(words, sorted, table, order, entries, pending, out var bits))
            {
                return false;
            }

            if (table.LinkAt < 0)
            {
                firstBits = bits;
            }
        }

        code = new PrefixCode(entries.ToArray(), firstBits, order, shortest, space == 1UL << LongestCode, -1);
        return true;
    }

    /// <summary>The symbol whose codeword <paramref name="next"/> begins by, and the codeword's length; -1, and a length
    /// of 0, where none does. With <see cref="PrefixBitOrder.HighFirst"/> the next bit is the highest of
    /// <paramref name="next"/>, with <see cref="PrefixBitOrder.LowFirst"/> the lowest; at least the longest codeword's
    /// length of them must be there, zeros past the end of the data.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ulong next, out int length)
    {
        // A codeword of the first bits, the most of any code, at once; a longer one apart, not to swell its callers.
        var index = (int)(next >> _firstShift) & _firstMask;
        var entry = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_entries), index);
        if (entry > 0)
        {
            length = entry & ((1 << LengthBits) - 1);
            return entry >> LengthBits;
        }

        return DecodeLonger(next, entry, out length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int DecodeLonger(ulong next, int entry, out int length)
    {
        ref var entries = ref MemoryMarshal.GetArrayDataReference(_entries);
        var consumed = _firstBits;
        if (entry == 0)
        {
            length = 0;
            return _onlySymbol;
        }

        var start = (-1 - entry) >> TableBitsBits;
        var bits = (-1 - entry) & ((1 << TableBitsBits) - 1);
        while (true)
        {
            var index = Order == PrefixBitOrder.HighFirst
                ? (int)(next << consumed >> (64 - bits))
                : (int)(next >> consumed) & ((1 << bits) - 1);
            var found = Unsafe.Add(ref entries, start + index);
            if (found > 0)
            {
                length = found & ((1 << LengthBits) - 1);
                return found >> LengthBits;
            }

            if (found == 0)
            {
                length = 0;
                return -1;
            }

            var link = -1 - found;
            consumed += bits;
            start = link >> TableBitsBits;
            bits = link & ((1 << TableBitsBits) - 1);
        }
    }

    // One table of the words that share their first bits: theirs that are no longer than it looked up at once, the
    // others in tables of their own, linked from it.
    private static bool Fill(ReadOnlySpan<PrefixCodeWord> words, int[] sorted, Pending table, PrefixBitOrder order,
        Entries entries, Stack<Pending> pending, out int bits)
    {
        var longest = 0;
        for (var at = table.From; at < table.To; at++)
        {
            longest = Math.Max(longest, words[sorted[at]].Length - table.Consumed);
        }

        bits = Math.Clamp(longest, 1, TableBits);
        var start = entries.Add(1 << bits);
        if (table.LinkAt >= 0)
        {
            entries[table.LinkAt] = -1 - (start << TableBitsBits | bits);
        }

        // The longer words of one slot are a run of the sorted ones: each run a table of its own.
        var runFrom = -1;
        var runSlot = -1;
        for (var position = table.From; position <= table.To; position++)
        {
            var isLonger = position < table.To && words[sorted[position]].Length - table.Consumed > bits;
            var slot = isLonger ? IndexOf(SliceOf(words[sorted[position]], table.Consumed, bits), bits, order) : -1;
            if (runFrom >= 0 && slot != runSlot)
            {
                if (entries[start + runSlot] != 0)
                {
                    return false;
                }

                // Taken, until its table is made.
                entries[start + runSlot] = -1;
                pending.Push(new Pending(runFrom, position, table.Consumed + bits, start + runSlot));
                runFrom = -1;
            }

            if (isLonger && runFrom < 0)
            {
                runFrom = position;
                runSlot = slot;
            }

            if (position == table.To || isLonger)
            {
                continue;
            }

            var word = words[sorted[position]];
            var rest = word.Length - table.Consumed;
            // All the entries its bits begin.
            var first = IndexOf(SliceOf(word, table.Consumed, rest), rest, order);
            var leaf = word.Symbol << LengthBits | word.Length;
            for (var fill = 0; fill < 1 << (bits - rest); fill++)
            {
                var entry = start + (order == PrefixBitOrder.HighFirst ? first << (bits - rest) | fill : first | fill << rest);
                if (entries[entry] != 0)
                {
                    return false;
                }

                entries[entry] = leaf;
            }
        }

        return true;
    }

    // Its bits from the consumed ones on, as many as asked: the first the highest.
    private static int SliceOf(PrefixCodeWord word, int consumed, int count) =>
        count == 0 ? 0 : (int)(word.Code >> (word.Length - consumed - count) & ((1u << count) - 1));

    // Where in a table bits of a codeword are: as they are, the first the highest, or turned round, the first the lowest.
    private static int IndexOf(int slice, int count, PrefixBitOrder order)
    {
        if (order == PrefixBitOrder.HighFirst)
        {
            return slice;
        }

        var reversed = 0;
        for (var bit = 0; bit < count; bit++)
        {
            reversed = reversed << 1 | (slice >> bit & 1);
        }

        return reversed;
    }

    private readonly record struct Pending(int From, int To, int Consumed, int LinkAt);

    // The tables' entries, one after another, in an array grown as they come.
    private sealed class Entries
    {
        private int[] _items = new int[1 << (TableBits + 1)];
        private int _count;

        public ref int this[int index] => ref _items[index];

        // Room for so many more, cleared; where they begin.
        public int Add(int count)
        {
            if (_count + count > _items.Length)
            {
                Array.Resize(ref _items, Math.Max(_items.Length * 2, _count + count));
            }

            var start = _count;
            _count += count;
            return start;
        }

        public int[] ToArray() => _items.AsSpan(0, _count).ToArray();
    }
}
