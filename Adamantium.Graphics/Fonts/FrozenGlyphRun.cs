using System;

namespace Adamantium.Graphics.Fonts;

/// <summary>An immutable snapshot of a <see cref="TextLayout"/>'s shaped glyphs in local coordinates, taken on the record
/// thread so rendering never reads the live layout. The shared <see cref="FontAtlas"/> tiles never move.</summary>
public sealed class FrozenGlyphRun(FontItem[] glyphs, int count, FontAtlas atlas, float fontSize, float effectReach = 0)
{
    public FontItem[] Glyphs { get; } = glyphs;
    public int Count { get; } = count;
    public FontAtlas Atlas { get; } = atlas;
    public float FontSize { get; } = fontSize;

    /// <summary>How far, at most, the run's <see cref="GlyphEffect"/>s move a glyph along either axis, in the layout's
    /// units; 0 without effects.</summary>
    public float EffectReach { get; } = effectReach;

    /// <summary>Screen-px reach of a glyph beyond its laid place: the atlas margin scaled to the font size, for an
    /// outline or glow, plus <see cref="EffectReach"/>. The direct/composite text target + composite quad are padded by
    /// it so edge glyphs aren't clipped. Computed from the FROZEN atlas + size so the applier never reads the live
    /// layout.</summary>
    public int EffectPadding => Atlas == null
        ? 0
        : (int)Math.Ceiling(Atlas.GlyphMargin * FontSize / Atlas.MSDFTextureSize + EffectReach);
}
