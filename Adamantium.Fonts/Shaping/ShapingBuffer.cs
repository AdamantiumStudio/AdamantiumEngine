namespace Adamantium.Fonts.Shaping;

/// <summary>The memory <see cref="TextShaper"/> shapes text in, kept by whoever shapes text again and again - a text
/// layout - so a shaping does not allocate its own. One shaping at a time uses it: one thread, as the layout keeping
/// it. A buffer grown past a long paragraph's size is let go after use rather than kept.</summary>
public sealed class ShapingBuffer
{
    private GlyphBuffer _glyphs;

    internal GlyphBuffer Take(int length)
    {
        var glyphs = _glyphs ?? new GlyphBuffer(length);
        _glyphs = null;
        glyphs.Reset(length);
        return glyphs;
    }

    internal void Give(GlyphBuffer glyphs)
    {
        _glyphs = glyphs.Keepable ? glyphs : null;
    }
}
