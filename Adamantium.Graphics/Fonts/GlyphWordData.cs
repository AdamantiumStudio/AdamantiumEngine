using System;
using Adamantium.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

public class GlyphWordData
{
    public GlyphWordData(Glyph glyph, Char symbol, RectangleF rect, int positionInString, int lineIndex)
    {
        Glyph = glyph;
        Rect = rect;
        Symbol = symbol;
        PositionInString = positionInString;
        LineIndex = lineIndex;
    }
    
    public Char Symbol { get; }

    public Glyph Glyph { get; internal set; }

    public RectangleF Rect;

    public int PositionInString { get; set; }
    
    public int LineIndex { get; set; }

    /// <summary>The pen position the glyph is drawn from, before its bearing and offset.</summary>
    public double PenX { get; set; }

    /// <summary>How far the pen moves after this glyph, kerning and positioning included.</summary>
    public double Advance { get; set; }

    public double OffsetX { get; set; }

    /// <summary>Upward from the baseline, as in the font.</summary>
    public double OffsetY { get; set; }

    /// <summary>The font the glyph belongs to.</summary>
    public IFont Font { get; set; }

    /// <summary>The size the glyph is set at.</summary>
    public double FontSize { get; set; }

    /// <summary>The attributes of the text this glyph draws; null for plain text.</summary>
    public TextAttributes Attributes { get; set; }

    /// <summary>How wide the glyph is drawn, as a share of its own width (<see cref="TextLayout.GlyphScaling"/>), about
    /// its origin; 1 draws it as the font has it.</summary>
    public double HorizontalScale { get; set; } = 1;

    /// <summary>Whether the glyph stands upright in a vertical line, shaped top to bottom. <see cref="PenX"/> and
    /// <see cref="Advance"/> then run down the line, and <see cref="Rect"/> is where the glyph is drawn.</summary>
    public bool Upright { get; set; }

    /// <summary>Whether the glyph lies turned 90° clockwise in a vertical line. <see cref="PenX"/> and
    /// <see cref="Advance"/> then run down the line, and <see cref="Rect"/> bounds the turned glyph.</summary>
    public bool Sideways { get; set; }

    /// <summary>Whether the glyph is a ruby annotation's, set beside its base: it takes none of the base's effects.</summary>
    public bool Annotates { get; init; }

    internal double Tail { get; init; }

    public override string ToString()
    {
        return $"{Symbol} [{Rect}]";
    }
}