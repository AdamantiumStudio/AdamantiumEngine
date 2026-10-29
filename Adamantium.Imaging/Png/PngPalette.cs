using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Png;

/// <summary>The distinct colors of a picture, up to 256 - RGBA of 8 bits packed in an int, red lowest - in a small
/// open-addressed table, so a pixel's color is found in a look or two: first gathered, then put in the palette's order
/// and asked for each pixel's index.</summary>
internal sealed class PngPalette
{
    /// <summary>Colors a palette holds at most.</summary>
    public const int MaxColors = 256;

    private const int Slots = 1024;

    private readonly uint[] _keys = new uint[Slots];
    private readonly short[] _indices = new short[Slots];
    private readonly List<uint> _colors = [];

    /// <summary>The colors, in the order <see cref="Order"/> puts them.</summary>
    public IReadOnlyList<uint> Colors => _colors;

    /// <summary>Adds <paramref name="color"/> unless it is here; false when it would be color 257 - the picture needs
    /// no palette then.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Add(uint color)
    {
        var slot = Find(color);
        if (_indices[slot] != 0)
        {
            return true;
        }

        if (_colors.Count == MaxColors)
        {
            return false;
        }

        _colors.Add(color);
        _keys[slot] = color;
        _indices[slot] = (short)_colors.Count;
        return true;
    }

    /// <summary>The palette index of <paramref name="color"/>, one of its colors.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(uint color) => _indices[Find(color)] - 1;

    /// <summary>Puts the translucent colors first, so tRNS can stop at the last of them, and orders by value after,
    /// whatever order the pixels came in.</summary>
    public void Order()
    {
        _colors.Sort((x, y) => x >> 24 != y >> 24 ? (x >> 24).CompareTo(y >> 24) : x.CompareTo(y));
        for (var i = 0; i < _colors.Count; i++)
        {
            _indices[Find(_colors[i])] = (short)(i + 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Find(uint color)
    {
        var slot = (int)((color * 0x9E3779B1u) >> 22);
        while (_indices[slot] != 0 && _keys[slot] != color)
        {
            slot = (slot + 1) & (Slots - 1);
        }

        return slot;
    }
}
