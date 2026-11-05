using System;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A line along a side of a paragraph, as Word's paragraph border: <see cref="Width"/> of
/// <see cref="Color"/>, <see cref="Space"/> away from the text, in the units the text is laid out in. Paragraphs one
/// after another with the same borders and margins share one frame round them all.</summary>
public sealed record ParagraphBorder
{
    private readonly double _width;
    private readonly double _space;

    /// <summary>The line that sets the side to none, over one a style under it draws.</summary>
    public static ParagraphBorder None { get; } = new(0, 0);

    /// <exception cref="ArgumentOutOfRangeException">The width or the space is below 0 or no number.</exception>
    public ParagraphBorder(double width, double space, Color? color = null,
        ParagraphBorderStyle style = ParagraphBorderStyle.Solid)
    {
        Width = width;
        Space = space;
        Color = color;
        Style = style;
    }

    /// <summary>How thick the line is; 0 draws none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0 or no number.</exception>
    public double Width
    {
        get => _width;
        init => _width = double.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A border is 0 or more wide.");
    }

    /// <summary>How far the line keeps from the text.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0 or no number.</exception>
    public double Space
    {
        get => _space;
        init => _space = double.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A border keeps 0 or more from the text.");
    }

    /// <summary>The line's color; null is the color of the text.</summary>
    public Color? Color { get; init; }

    /// <summary>How the line is drawn: solid, dotted or dashed, or double - two lines of <see cref="Width"/>, as far
    /// apart, as Word's.</summary>
    public ParagraphBorderStyle Style { get; init; }

    /// <summary>Whether a line is drawn.</summary>
    public bool IsVisible => Width > 0;

    /// <summary>How thick the line is drawn: <see cref="Width"/>, three times it for a double line.</summary>
    public double Thickness => Style == ParagraphBorderStyle.Double ? 3 * Width : Width;

    /// <summary>The room the side takes beside the text: the line and its space; none for no line.</summary>
    public double Room => IsVisible ? Thickness + Space : 0;
}
