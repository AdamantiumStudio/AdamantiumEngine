namespace Adamantium.Graphics.Fonts;

/// <summary>An immutable snapshot of a <see cref="TextLayout"/>'s shaped glyphs in local coordinates, taken on the record
/// thread so rendering never reads the live layout. The shared <see cref="FontAtlas"/> tiles never move.</summary>
public sealed class FrozenGlyphRun(FontItem[] glyphs, int count, FontAtlas atlas, float fontSize)
{
    public FontItem[] Glyphs { get; } = glyphs;
    public int Count { get; } = count;
    public FontAtlas Atlas { get; } = atlas;
    public float FontSize { get; } = fontSize;

    /// <summary>The atlas's <see cref="FontAtlas.Version"/> the run was built at.</summary>
    public int AtlasVersion { get; init; }

    /// <summary>Glyphs have landed in the atlas since the run was built: what each holder of a run of a layout shared
    /// with others rebuilds on.</summary>
    public bool IsStale => Atlas != null && (Atlas.IsDisposed || Atlas.Version != AtlasVersion);
}
