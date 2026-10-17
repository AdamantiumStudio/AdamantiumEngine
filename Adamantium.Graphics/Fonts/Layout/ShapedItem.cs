using Adamantium.Fonts;

namespace Adamantium.Graphics.Fonts.Layout;

internal readonly struct ShapedItem
{
    public ShapedItem(Glyph glyph, char symbol, int cluster, double advance, double offsetX, double offsetY,
        TextAttributes attributes, IFont font, double fontSize, bool upright = false, double horizontalScale = 1,
        double tail = 0)
    {
        Upright = upright;
        Tail = tail;
        HorizontalScale = horizontalScale;
        Font = font;
        Glyph = glyph;
        Symbol = symbol;
        Cluster = cluster;
        Advance = advance;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Attributes = attributes;
        FontSize = fontSize;
    }

    public IFont Font { get; }

    public double FontSize { get; }

    public Glyph Glyph { get; }

    public char Symbol { get; }

    public int Cluster { get; }

    public double Advance { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public TextAttributes Attributes { get; }

    public bool Upright { get; }

    public double HorizontalScale { get; }

    public double Tail { get; }
}
