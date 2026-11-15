using System;
using System.Numerics;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The frequencies a dynamic context has counted (ECMA-363 10.1, 10.4.9), summed in a binary indexed tree;
/// symbol 0 is the escape.</summary>
internal sealed class U3dDynamicHistogram
{
    /// <summary>At this many occurrences every frequency is halved and the escape counted once more.</summary>
    internal const uint RescaleTotal = 0x1FFF;

    /// <summary>Symbols from this one up are never counted, as Annex A has it.</summary>
    internal const uint FirstUncountedSymbol = 0xFFFF;

    private const int GrowthStep = 32;

    private int[] _counts;
    private int[] _tree;
    private uint _total;

    internal U3dDynamicHistogram()
    {
        _counts = new int[GrowthStep];
        _counts[0] = 1;
        _total = 1;
        Rebuild();
    }

    /// <summary>The occurrences of every symbol, the escape's among them.</summary>
    internal uint Total => _total;

    /// <summary>How often the symbol has occurred.</summary>
    internal uint GetFrequency(uint symbol) => symbol < (uint)_counts.Length ? (uint)_counts[symbol] : 0;

    /// <summary>The occurrences of every symbol below this one.</summary>
    internal uint GetCumulativeFrequency(uint symbol) => symbol < (uint)_counts.Length ? GetPrefixSum((int)symbol) : _total;

    /// <summary>The highest symbol with no more than <paramref name="cumulative"/> occurrences below it.</summary>
    internal uint GetSymbolAt(uint cumulative)
    {
        var position = 0;
        var remaining = cumulative;
        for (var step = 1 << (31 - BitOperations.LeadingZeroCount((uint)_counts.Length)); step > 0; step >>= 1)
        {
            var next = position + step;
            if (next <= _counts.Length && (uint)_tree[next] <= remaining)
            {
                position = next;
                remaining -= (uint)_tree[next];
            }
        }

        return (uint)position;
    }

    /// <summary>Counts one more occurrence of the symbol.</summary>
    internal void Add(uint symbol)
    {
        if (symbol >= FirstUncountedSymbol)
        {
            return;
        }

        if (symbol >= (uint)_counts.Length)
        {
            Grow((int)symbol + 1);
        }

        if (_total >= RescaleTotal)
        {
            Rescale();
        }

        _counts[symbol]++;
        _total++;
        for (var index = (int)symbol + 1; index <= _counts.Length; index += index & -index)
        {
            _tree[index]++;
        }
    }

    private uint GetPrefixSum(int count)
    {
        var sum = 0;
        for (var index = count; index > 0; index -= index & -index)
        {
            sum += _tree[index];
        }

        return (uint)sum;
    }

    private void Grow(int minimum)
    {
        var grown = new int[Math.Max(minimum + GrowthStep, _counts.Length * 2)];
        _counts.CopyTo(grown, 0);
        _counts = grown;
        Rebuild();
    }

    private void Rescale()
    {
        _total = 0;
        for (var index = 0; index < _counts.Length; index++)
        {
            _counts[index] >>= 1;
        }

        _counts[0]++;
        foreach (var count in _counts)
        {
            _total += (uint)count;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        _tree = new int[_counts.Length + 1];
        for (var index = 1; index <= _counts.Length; index++)
        {
            _tree[index] += _counts[index - 1];
            var parent = index + (index & -index);
            if (parent <= _counts.Length)
            {
                _tree[parent] += _tree[index];
            }
        }
    }
}
