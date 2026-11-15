using System;
using System.Numerics;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The frequencies a dynamic context has counted (ECMA-363 10.1, 10.4.9): symbol 0 is the escape, counted once
/// from the start; a value is the symbol one above it. Frequencies below a symbol are summed in a binary indexed tree,
/// so a context whose values run into the thousands - quantized position differences - costs a logarithm per symbol,
/// not a walk over every symbol below it.</summary>
internal sealed class U3dDynamicHistogram
{
    /// <summary>At this many occurrences every frequency is halved, rounding down, and the escape counted once more -
    /// before the next symbol is counted.</summary>
    internal const uint RescaleTotal = 0x1FFF;

    /// <summary>Symbols from this one up are never counted. The clause's text stops above 0xFFFF; its own
    /// implementation (Annex A, which every encoder is asked to agree with byte for byte) stops at 0xFFFF itself, and
    /// that is the one files are written by.</summary>
    internal const uint FirstUncountedSymbol = 0xFFFF;

    private const int GrowthStep = 32;

    private int[] counts;
    private int[] tree;
    private uint total;

    public U3dDynamicHistogram()
    {
        counts = new int[GrowthStep];
        counts[0] = 1;
        total = 1;
        Rebuild();
    }

    /// <summary>The occurrences of every symbol, the escape's among them.</summary>
    public uint Total => total;

    /// <summary>How often the symbol has occurred.</summary>
    public uint Frequency(uint symbol) => symbol < (uint)counts.Length ? (uint)counts[symbol] : 0;

    /// <summary>The occurrences of every symbol below this one.</summary>
    public uint CumulativeFrequency(uint symbol) => symbol < (uint)counts.Length ? Prefix((int)symbol) : total;

    /// <summary>The symbol whose share of the occurrences holds <paramref name="cumulative"/>: the highest one with no
    /// more than that many occurrences below it. <paramref name="cumulative"/> must be below <see cref="Total"/>.</summary>
    public uint SymbolAt(uint cumulative)
    {
        var position = 0;
        var remaining = cumulative;
        for (var step = 1 << (31 - BitOperations.LeadingZeroCount((uint)counts.Length)); step > 0; step >>= 1)
        {
            var next = position + step;
            if (next <= counts.Length && (uint)tree[next] <= remaining)
            {
                position = next;
                remaining -= (uint)tree[next];
            }
        }

        return (uint)position;
    }

    /// <summary>Counts one more occurrence of the symbol.</summary>
    public void Add(uint symbol)
    {
        if (symbol >= FirstUncountedSymbol)
        {
            return;
        }

        if (symbol >= (uint)counts.Length)
        {
            Grow((int)symbol + 1);
        }

        if (total >= RescaleTotal)
        {
            Rescale();
        }

        counts[symbol]++;
        total++;
        for (var index = (int)symbol + 1; index <= counts.Length; index += index & -index)
        {
            tree[index]++;
        }
    }

    private uint Prefix(int count)
    {
        var sum = 0;
        for (var index = count; index > 0; index -= index & -index)
        {
            sum += tree[index];
        }

        return (uint)sum;
    }

    private void Grow(int minimum)
    {
        var grown = new int[Math.Max(minimum + GrowthStep, counts.Length * 2)];
        counts.CopyTo(grown, 0);
        counts = grown;
        Rebuild();
    }

    private void Rescale()
    {
        total = 0;
        for (var index = 0; index < counts.Length; index++)
        {
            counts[index] >>= 1;
        }

        counts[0]++;
        foreach (var count in counts)
        {
            total += (uint)count;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        tree = new int[counts.Length + 1];
        for (var index = 1; index <= counts.Length; index++)
        {
            tree[index] += counts[index - 1];
            var parent = index + (index & -index);
            if (parent <= counts.Length)
            {
                tree[parent] += tree[index];
            }
        }
    }
}
