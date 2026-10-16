using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>An immutable snapshot of a <see cref="TextLayout"/>'s shaped glyphs in local coordinates, taken on the record
/// thread so rendering never reads the live layout. The shared <see cref="FontAtlas"/> tiles never move.</summary>
public sealed class FrozenGlyphRun(FontItem[] glyphs, int count, FontAtlas atlas, float fontSize)
{
    private const float MinPerspectiveW = 0.05f;

    /// <summary>The farthest <see cref="ReachOf"/> answers: a glyph tipped behind the viewer has no place to measure.</summary>
    public const float MaxReach = 4096;

    public FontItem[] Glyphs { get; } = glyphs;
    public int Count { get; } = count;
    public FontAtlas Atlas { get; } = atlas;
    public float FontSize { get; } = fontSize;

    /// <summary>Screen-px reach of a glyph beyond its laid place: the atlas margin scaled to the font size, for an
    /// outline or glow, plus how far <paramref name="effects"/> move one (see <see cref="ReachOf"/>). The text target and
    /// its composite quad are padded by it so edge glyphs aren't clipped.</summary>
    public int PaddingFor(IReadOnlyList<GlyphEffect> effects) => Atlas == null
        ? 0
        : (int)Math.Ceiling(Atlas.GlyphMargin * FontSize / Atlas.MSDFTextureSize + ReachOf(effects));

    /// <summary>How far, at most, <paramref name="effects"/> move a glyph of the run along either axis, in the layout's
    /// units; 0 without effects, <see cref="MaxReach"/> when one tips a glyph behind the viewer.</summary>
    public float ReachOf(IReadOnlyList<GlyphEffect> effects)
    {
        if (effects == null || effects.Count == 0)
        {
            return 0;
        }

        var reach = 0f;
        for (var i = 0; i < Count; i++)
        {
            ref readonly var glyph = ref Glyphs[i];
            if (!GlyphEffect.Compose(effects, glyph.Place.X, glyph.Cell, out var total) || total.IsIdentity)
            {
                continue;
            }

            var cell = new RectangleF(glyph.Cell.X, glyph.Cell.Y, glyph.Cell.Z, glyph.Cell.W);
            if (glyph.Paint.X == 0)
            {
                var quad = glyph.ArrangeRect;
                cell = RectangleF.Union(cell, new RectangleF(quad.X, quad.Y, quad.Z, quad.W));
            }

            reach = Math.Max(reach, Reach(cell, total));
            if (reach >= MaxReach)
            {
                return MaxReach;
            }
        }

        return reach;
    }

    private static float Reach(RectangleF cell, Matrix3x3F total)
    {
        var reach = 0f;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? cell.Left : cell.Right;
            var y = (corner & 2) == 0 ? cell.Top : cell.Bottom;
            var w = x * total.M13 + y * total.M23 + total.M33;
            if (w < MinPerspectiveW)
            {
                return MaxReach;
            }

            var movedX = (x * total.M11 + y * total.M21 + total.M31) / w;
            var movedY = (x * total.M12 + y * total.M22 + total.M32) / w;
            reach = Math.Max(reach, Math.Max(Math.Abs(movedX - x), Math.Abs(movedY - y)));
        }

        return Math.Min(reach, MaxReach);
    }
}
