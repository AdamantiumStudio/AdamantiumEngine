using System.Collections.Generic;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A dynamic context's frequencies as clause 10 defines them: the escape counted once from the start, a value
/// the symbol above it.</summary>
internal sealed class U3dTestHistogram
{
    private const uint RescaleTotal = 0x1FFF;
    private const uint FirstUncountedSymbol = 0xFFFF;

    private readonly List<uint> _counts = [1];

    public uint Total { get; private set; } = 1;

    public uint GetFrequency(uint symbol) => symbol < _counts.Count ? _counts[(int)symbol] : 0;

    public uint GetCumulative(uint symbol)
    {
        var sum = 0u;
        for (var index = 0; index < symbol && index < _counts.Count; index++)
        {
            sum += _counts[index];
        }

        return sum;
    }

    public void Add(uint symbol)
    {
        if (symbol >= FirstUncountedSymbol)
        {
            return;
        }

        while (_counts.Count <= symbol)
        {
            _counts.Add(0);
        }

        if (Total >= RescaleTotal)
        {
            Total = 0;
            for (var index = 0; index < _counts.Count; index++)
            {
                _counts[index] /= 2;
            }

            _counts[0]++;
            foreach (var count in _counts)
            {
                Total += count;
            }
        }

        _counts[(int)symbol]++;
        Total++;
    }
}
