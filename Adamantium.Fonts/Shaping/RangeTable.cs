using System;
using System.Collections.Generic;
using System.Globalization;

namespace Adamantium.Fonts.Shaping;

internal sealed class RangeTable<T>
{
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly T[] _values;
    private readonly T _missing;
    private readonly T[] _latin = new T[256];

    private RangeTable(int[] starts, int[] ends, T[] values, T missing)
    {
        _starts = starts;
        _ends = ends;
        _values = values;
        _missing = missing;
        for (var codepoint = 0; codepoint < _latin.Length; codepoint++)
        {
            _latin[codepoint] = Search(codepoint);
        }
    }

    public T this[int codepoint] => (uint)codepoint < (uint)_latin.Length ? _latin[codepoint] : Search(codepoint);

    private T Search(int codepoint)
    {
        var index = Array.BinarySearch(_starts, codepoint);
        if (index < 0)
        {
            index = ~index - 1;
        }

        if (index < 0 || codepoint > _ends[index])
        {
            return _missing;
        }

        return _values[index];
    }

    public static RangeTable<T> Load(string resource, Func<string, T> parse, T missing)
    {
        var starts = new List<int>();
        var ends = new List<int>();
        var values = new List<T>();
        foreach (var line in DataFile.ReadLines(resource))
        {
            var fields = line.Split(';');
            var range = fields[0].Split([".."], StringSplitOptions.None);
            starts.Add(int.Parse(range[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            ends.Add(int.Parse(range[range.Length - 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            values.Add(fields.Length > 1 ? parse(fields[1]) : parse(null));
        }

        return new RangeTable<T>(starts.ToArray(), ends.ToArray(), values.ToArray(), missing);
    }
}
