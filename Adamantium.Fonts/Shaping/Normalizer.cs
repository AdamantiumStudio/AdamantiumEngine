using System;
using System.Globalization;

namespace Adamantium.Fonts.Shaping;

internal struct Normalizer
{
    private const int MaxCombiningMarks = 32;

    private GlyphBuffer _output;
    private GlyphInfo[] _input;
    private IFont _font;
    private GlyphBuffer _buffer;
    private bool _arabicMarks;
    private bool _hebrewForms;

    // The presentation forms of the Hebrew letters with a dagesh, from alef: 0 where there is none.
    private static readonly int[] DageshForms =
    [
        0xFB30, 0xFB31, 0xFB32, 0xFB33, 0xFB34, 0xFB35, 0xFB36, 0, 0xFB38, 0xFB39, 0xFB3A, 0xFB3B, 0xFB3C, 0, 0xFB3E, 0,
        0xFB40, 0xFB41, 0, 0xFB43, 0xFB44, 0, 0xFB46, 0xFB47, 0xFB48, 0xFB49, 0xFB4A,
    ];

    public static void Normalize(IFont font, GlyphBuffer buffer, bool arabicMarks = false, bool hebrewForms = false)
    {
        var normalizer = new Normalizer
        {
            _font = font,
            _buffer = buffer,
            _arabicMarks = arabicMarks,
            _hebrewForms = hebrewForms,
            _output = new GlyphBuffer(buffer.Length),
            _input = GlyphBuffer.Rent<GlyphInfo>(Math.Max(buffer.Length, 4)),
        };
        try
        {
            normalizer.Run();
        }
        finally
        {
            normalizer._output.Dispose();
            GlyphBuffer.Return(normalizer._input);
        }
    }

    private void Run()
    {
        var length = _buffer.Length;
        var input = Input(length);
        Array.Copy(_buffer.Info, input, length);

        var allSimple = Decompose(input, length);
        if (!allSimple)
        {
            Reorder();
            UnhideIdleJoiners();
            Recompose();
        }

        _buffer.Clear();
        for (var i = 0; i < _output.Length; i++)
        {
            _buffer.Add(_output.Info[i]);
        }
    }

    // A combining grapheme joiner that kept no marks from being reordered is skipped by lookups as any default
    // ignorable is, as HarfBuzz unhides it.
    private readonly void UnhideIdleJoiners()
    {
        var output = _output.Info;
        for (var i = 1; i + 1 < _output.Length; i++)
        {
            if (output[i].Codepoint == 0x034F && (output[i + 1].CombiningClass == 0
                                                  || output[i - 1].CombiningClass <= output[i + 1].CombiningClass))
            {
                output[i].Flags &= ~UnicodeFlags.Hidden;
            }
        }
    }

    private GlyphInfo[] Input(int length)
    {
        if (_input.Length < length)
        {
            var old = _input;
            _input = GlyphBuffer.Rent<GlyphInfo>(length);
            GlyphBuffer.Return(old);
        }

        return _input;
    }

    private bool Decompose(GlyphInfo[] input, int length)
    {
        var allSimple = true;
        var index = 0;
        while (index < length)
        {
            var end = index + 1;
            while (end < length && !input[end].IsUnicodeMark)
            {
                end++;
            }

            if (end < length)
            {
                end--;
            }

            for (; index < end; index++)
            {
                DecomposeCurrent(input[index], true);
            }

            if (index == length)
            {
                break;
            }

            allSimple = false;
            end = index + 1;
            while (end < length && input[end].IsUnicodeMark)
            {
                end++;
            }

            DecomposeCluster(input, index, end);
            index = end;
        }

        return allSimple;
    }

    private void DecomposeCluster(GlyphInfo[] input, int start, int end)
    {
        if (start + 1 == end)
        {
            DecomposeCurrent(input[start], true);
            return;
        }

        for (var i = start; i < end; i++)
        {
            if (UnicodeProps.IsVariationSelector(input[i].Codepoint))
            {
                MapVariationSequences(input, start, end);
                return;
            }
        }

        for (var i = start; i < end; i++)
        {
            DecomposeCurrent(input[i], false);
        }
    }

    private void MapVariationSequences(GlyphInfo[] input, int start, int end)
    {
        var i = start;
        while (i < end - 1)
        {
            if (!UnicodeProps.IsVariationSelector(input[i + 1].Codepoint))
            {
                Output(input[i], MapGlyph(input[i].Codepoint));
                i++;
                continue;
            }

            if (_font.TryGetGlyphIndex(input[i].Codepoint, input[i + 1].Codepoint, out var variant))
            {
                var info = input[i];
                info.Cluster = Math.Min(info.Cluster, input[i + 1].Cluster);
                Output(info, variant);
            }
            else
            {
                Output(input[i], MapGlyph(input[i].Codepoint));
                Output(input[i + 1], MapGlyph(input[i + 1].Codepoint));
            }

            i += 2;
            while (i < end && UnicodeProps.IsVariationSelector(input[i].Codepoint))
            {
                Output(input[i], MapGlyph(input[i].Codepoint));
                i++;
            }
        }

        if (i < end)
        {
            Output(input[i], MapGlyph(input[i].Codepoint));
        }
    }

    private void DecomposeCurrent(GlyphInfo info, bool shortest)
    {
        var codepoint = info.Codepoint;
        if (shortest && _font.TryGetGlyphIndex(codepoint, out var glyph))
        {
            Output(info, glyph);
            return;
        }

        if (DecomposeInto(info, shortest, codepoint) > 0)
        {
            return;
        }

        if (!shortest && _font.TryGetGlyphIndex(codepoint, out glyph))
        {
            Output(info, glyph);
            return;
        }

        if (info.Category == UnicodeCategory.SpaceSeparator)
        {
            var kind = UnicodeProps.SpaceFallback(codepoint);
            if (kind != SpaceKind.NotSpace && _font.TryGetGlyphIndex(0x0020, out var space))
            {
                info.Space = kind;
                _buffer.HasSpaceFallback = true;
                Output(info, space);
                return;
            }
        }

        if (codepoint == 0x2011 && _font.TryGetGlyphIndex(0x2010, out var hyphen))
        {
            Output(info, hyphen);
            return;
        }

        Output(info, 0);
    }

    private int DecomposeInto(GlyphInfo info, bool shortest, int codepoint)
    {
        if (!UnicodeData.TryDecompose(codepoint, out var first, out var second))
        {
            return 0;
        }

        uint secondGlyph = 0;
        if (second != 0 && !_font.TryGetGlyphIndex(second, out secondGlyph))
        {
            return 0;
        }

        var hasFirst = _font.TryGetGlyphIndex(first, out var firstGlyph);
        if (shortest && hasFirst)
        {
            return OutputPair(info, first, firstGlyph, second, secondGlyph);
        }

        var count = DecomposeInto(info, shortest, first);
        if (count > 0)
        {
            if (second != 0)
            {
                OutputCharacter(info, second, secondGlyph);
                return count + 1;
            }

            return count;
        }

        return hasFirst ? OutputPair(info, first, firstGlyph, second, secondGlyph) : 0;
    }

    private int OutputPair(GlyphInfo info, int first, uint firstGlyph, int second, uint secondGlyph)
    {
        OutputCharacter(info, first, firstGlyph);
        if (second == 0)
        {
            return 1;
        }

        OutputCharacter(info, second, secondGlyph);
        return 2;
    }

    private readonly void OutputCharacter(GlyphInfo info, int codepoint, uint glyph)
    {
        info.Codepoint = codepoint;
        info.Glyph = glyph;
        UnicodeProps.Set(ref info, _buffer);
        _output.Add(info);
    }

    private readonly void Output(GlyphInfo info, uint glyph)
    {
        info.Glyph = glyph;
        _output.Add(info);
    }

    private readonly uint MapGlyph(int codepoint) => _font.TryGetGlyphIndex(codepoint, out var glyph) ? glyph : 0;

    private readonly void Reorder()
    {
        for (var i = 0; i < _output.Length; i++)
        {
            if (_output.Info[i].CombiningClass == 0)
            {
                continue;
            }

            var end = i + 1;
            while (end < _output.Length && _output.Info[end].CombiningClass != 0)
            {
                end++;
            }

            if (end - i <= MaxCombiningMarks)
            {
                SortByCombiningClass(i, end);
                if (_arabicMarks)
                {
                    ArabicShaper.ReorderMarks(_output, i, end);
                }
            }

            i = end;
        }
    }

    private readonly void SortByCombiningClass(int start, int end)
    {
        for (var i = start + 1; i < end; i++)
        {
            var j = i;
            while (j > start && _output.Info[j - 1].CombiningClass > _output.Info[i].CombiningClass)
            {
                j--;
            }

            if (j == i)
            {
                continue;
            }

            MergeClusters(j, i + 1);
            _output.Move(i, j);
        }
    }

    private void Recompose()
    {
        var length = _output.Length;
        var input = Input(length);
        Array.Copy(_output.Info, input, length);
        _output.Clear();
        _output.Add(input[0]);
        var starter = 0;
        for (var i = 1; i < length; i++)
        {
            var current = input[i];
            var output = _output.Info;
            if (current.IsUnicodeMark
                && (starter == _output.Length - 1 || output[_output.Length - 1].CombiningClass < current.CombiningClass)
                && (UnicodeData.TryCompose(output[starter].Codepoint, current.Codepoint, out var composed)
                    || (_hebrewForms && TryComposeHebrew(output[starter].Codepoint, current.Codepoint, out composed)))
                && _font.TryGetGlyphIndex(composed, out var glyph))
            {
                _output.Add(current);
                MergeClusters(starter, _output.Length);
                _output.RemoveAt(_output.Length - 1);
                ref var combined = ref _output.Info[starter];
                combined.Codepoint = composed;
                combined.Glyph = glyph;
                UnicodeProps.Set(ref combined, _buffer);
                continue;
            }

            _output.Add(current);
            if (current.CombiningClass == 0)
            {
                starter = _output.Length - 1;
            }
        }
    }

    // The Hebrew presentation forms Unicode leaves out of composition, as HarfBuzz composes them for a font that cannot
    // position the points itself.
    private static bool TryComposeHebrew(int letter, int point, out int composed)
    {
        composed = point switch
        {
            0x05B4 when letter == 0x05D9 => 0xFB1D,
            0x05B7 when letter == 0x05F2 => 0xFB1F,
            0x05B7 when letter == 0x05D0 => 0xFB2E,
            0x05B8 when letter == 0x05D0 => 0xFB2F,
            0x05B9 when letter == 0x05D5 => 0xFB4B,
            0x05BC when letter is >= 0x05D0 and <= 0x05EA => DageshForms[letter - 0x05D0],
            0x05BC when letter == 0xFB2A => 0xFB2C,
            0x05BC when letter == 0xFB2B => 0xFB2D,
            0x05BF when letter == 0x05D1 => 0xFB4C,
            0x05BF when letter == 0x05DB => 0xFB4D,
            0x05BF when letter == 0x05E4 => 0xFB4E,
            0x05C1 when letter == 0x05E9 => 0xFB2A,
            0x05C1 when letter == 0xFB49 => 0xFB2C,
            0x05C2 when letter == 0x05E9 => 0xFB2B,
            0x05C2 when letter == 0xFB49 => 0xFB2D,
            _ => 0,
        };
        return composed != 0;
    }

    private readonly void MergeClusters(int start, int end)
    {
        if (end - start < 2)
        {
            return;
        }

        var output = _output.Info;
        var cluster = output[start].Cluster;
        for (var i = start + 1; i < end; i++)
        {
            cluster = Math.Min(cluster, output[i].Cluster);
        }

        for (var i = start; i < end; i++)
        {
            output[i].Cluster = cluster;
        }
    }
}
