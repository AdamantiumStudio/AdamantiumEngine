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

    /// <summary>Whether the caller placed the glyph (<see cref="TextLayout.ProcessGlyphs"/>): drawn exactly where it
    /// stands, never snapped to the pixel grid.</summary>
    public bool Placed { get; init; }

    internal double Tail { get; init; }

    /// <summary>Where the glyph's outline is drawn, as the screen draws it: from the font's units, y up, to the layout's
    /// space, y down - at its pen on its baseline, at its size, stretched by <see cref="HorizontalScale"/>, slanted by a
    /// synthesized italic and turned for <see cref="Sideways"/>. Not for a glyph without a <see cref="Font"/> or a
    /// <see cref="Glyph"/>.</summary>
    public Matrix3x2<double> OutlineTransform
    {
        get
        {
            var scale = FontSize / Font.UnitsPerEm;
            // Slanted in the glyph's own frame, before a turn - the screen slants every glyph of an italic's stretch.
            var slant = Layout.TextGeometry.IsSlanted(Attributes) ? FontSynthesisRules.Slant : 0;
            var drawn = new Matrix3x2<double>(scale * HorizontalScale, 0, slant * scale, -scale,
                Layout.TextGeometry.Pen(this), Layout.TextGeometry.Baseline(this));
            if (!Sideways)
            {
                return drawn;
            }

            // Turned a quarter clockwise about the corner of where it is drawn, as the screen turns it.
            var corner = Layout.TextGeometry.DrawnRect(this);
            var turn = new Matrix3x2<double>(0, 1, -1, 0, corner.X + corner.Y, corner.Y - corner.X);
            return Matrix3x2<double>.Multiply(drawn, turn);
        }
    }

    public override string ToString()
    {
        return $"{Symbol} [{Rect}]";
    }
}