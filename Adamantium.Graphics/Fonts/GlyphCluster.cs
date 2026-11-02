using System;
using System.Collections.Generic;

namespace Adamantium.Graphics.Fonts;

/// <summary>A glyph of laid out text with the marks drawn over it, which move as one: the characters they draw, the line
/// they stand on and the middle of the glyph's drawn box across the layout.</summary>
/// <param name="Start">The first UTF-16 character the cluster draws.</param>
/// <param name="Length">How many characters it draws.</param>
/// <param name="Line">The line of the layout it stands on, from 0.</param>
/// <param name="X">The middle of its glyph's drawn box across the layout.</param>
public readonly record struct GlyphCluster(int Start, int Length, int Line, double X)
{
    /// <summary>The clusters of <paramref name="glyphs"/>, in their order: a glyph that moves the pen starts one, unless
    /// one starts at its character already - a vowel drawn in two parts - and the marks after it, which move nothing, go
    /// with it. Each takes the characters up to the next one's start, the first from the text's start, so every glyph
    /// moves with one.</summary>
    internal static GlyphCluster[] Of(IReadOnlyList<GlyphWordData> glyphs, int textLength)
    {
        var clusters = new List<GlyphCluster>();
        var byStart = new Dictionary<int, int>();
        foreach (var glyph in glyphs)
        {
            var start = glyph.PositionInString;
            if (start < 0 || byStart.ContainsKey(start) || glyph.Advance <= 0 && clusters.Count > 0)
            {
                continue;
            }

            byStart[start] = clusters.Count;
            clusters.Add(new GlyphCluster(start, 0, glyph.LineIndex, glyph.Rect.X + glyph.Rect.Width / 2.0));
        }

        var starts = new int[clusters.Count];
        byStart.Keys.CopyTo(starts, 0);
        Array.Sort(starts);
        for (var k = 0; k < starts.Length; k++)
        {
            var i = byStart[starts[k]];
            var from = k == 0 ? 0 : starts[k];
            var end = k + 1 < starts.Length ? starts[k + 1] : textLength;
            clusters[i] = clusters[i] with { Start = from, Length = Math.Max(1, end - from) };
        }

        return clusters.ToArray();
    }
}
