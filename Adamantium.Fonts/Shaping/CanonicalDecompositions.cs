using System.Collections.Generic;
using System.Globalization;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Shaping;

internal sealed class CanonicalDecompositions
{
    private readonly Dictionary<int, (int First, int Second)> _decompositions = new();
    private readonly Dictionary<long, int> _compositions = new(PairKeyComparer.Instance);

    public static CanonicalDecompositions Load()
    {
        var table = new CanonicalDecompositions();
        foreach (var line in DataFile.ReadLines("Decompositions.ucd"))
        {
            var fields = line.Split(';');
            var codepoint = Parse(fields[0]);
            var first = Parse(fields[1]);
            var second = fields[2].Length > 0 ? Parse(fields[2]) : 0;
            table._decompositions[codepoint] = (first, second);
            if (second != 0 && fields[3] == "0")
            {
                table._compositions[Key(first, second)] = codepoint;
            }
        }

        return table;
    }

    public bool TryDecompose(int codepoint, out int first, out int second)
    {
        if (_decompositions.TryGetValue(codepoint, out var pair))
        {
            first = pair.First;
            second = pair.Second;
            return true;
        }

        first = 0;
        second = 0;
        return false;
    }

    public bool TryCompose(int first, int second, out int composed)
    {
        return _compositions.TryGetValue(Key(first, second), out composed);
    }

    private static long Key(int first, int second) => ((long)first << 32) | (uint)second;

    private static int Parse(string hex) => int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
