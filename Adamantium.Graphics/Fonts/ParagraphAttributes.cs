using System;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>How a paragraph stands on the page. Lengths are in the units the text is laid out in; one that is no
/// number counts as none, and a line never starts past the layout's edges. A field not set takes what a paragraph
/// style under it says (<see cref="With"/>).</summary>
public sealed record ParagraphAttributes
{
    private HorizontalTextAlignment? _alignment;
    private double? _marginLeft;
    private double? _marginRight;
    private double? _spaceBefore;
    private double? _spaceAfter;
    private double? _textIndent;
    private double? _lineHeight;
    private ParagraphBorder _borderTop;
    private ParagraphBorder _borderBottom;
    private ParagraphBorder _borderLeft;
    private ParagraphBorder _borderRight;
    private double? _bordersContinueAbove;
    private double? _bordersContinueBelow;
    private Color? _shading;
    private double? _shadingContinuesBelow;
    private double? _markRoom;

    /// <summary>How its lines stand between its edges; null keeps the layout's alignment.</summary>
    public HorizontalTextAlignment? Alignment { get => _alignment; init => _alignment = value; }

    /// <summary>How far its lines keep from the left edge.</summary>
    public double MarginLeft { get => _marginLeft ?? 0; init => _marginLeft = value; }

    /// <summary>How far its lines keep from the right edge.</summary>
    public double MarginRight { get => _marginRight ?? 0; init => _marginRight = value; }

    /// <summary>Room above it. Between two paragraphs only the larger of this and the one above's
    /// <see cref="SpaceAfter"/> is kept; none above the first, nor at the top of a frame.</summary>
    public double SpaceBefore { get => _spaceBefore ?? 0; init => _spaceBefore = value; }

    /// <summary>Room below it, kept as <see cref="SpaceBefore"/> is; none below the last.</summary>
    public double SpaceAfter { get => _spaceAfter ?? 0; init => _spaceAfter = value; }

    /// <summary>How much further its first line starts than the rest, at the side its lines start from; negative hangs
    /// it out into the margin. A drop cap takes its place.</summary>
    public double TextIndent { get => _textIndent ?? 0; init => _textIndent = value; }

    /// <summary>The height of its lines, as the layout's line height: larger text may still grow a line. NaN keeps the
    /// layout's.</summary>
    public double LineHeight { get => _lineHeight ?? double.NaN; init => _lineHeight = value; }

    /// <summary>The line above it: the room it takes is kept above the first line, at the top of a frame too, unlike
    /// <see cref="SpaceBefore"/>. Horizontal text only.</summary>
    public ParagraphBorder BorderTop { get => _borderTop; init => _borderTop = value; }

    /// <summary>The line below it, its room kept below the last line.</summary>
    public ParagraphBorder BorderBottom { get => _borderBottom; init => _borderBottom = value; }

    /// <summary>The line left of it, in its left margin: it does not move the text.</summary>
    public ParagraphBorder BorderLeft { get => _borderLeft; init => _borderLeft = value; }

    /// <summary>The line right of it, in its right margin.</summary>
    public ParagraphBorder BorderRight { get => _borderRight; init => _borderRight = value; }

    /// <summary>For the layout's first paragraph: its frame goes on from a paragraph laid out elsewhere above, as one
    /// it shares with the paragraph before it, its side lines reaching this far above its first line. It draws no line
    /// above nor keeps room for one. Null closes the frame at its top.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0 or no number.</exception>
    public double? BordersContinueAbove
    {
        get => _bordersContinueAbove;
        init => _bordersContinueAbove = Reach(value);
    }

    /// <summary>For the layout's last paragraph, laid out to its end: its frame goes on to a paragraph laid out
    /// elsewhere below, its side lines reaching this far below its last line. It draws no line below nor keeps room for
    /// one. Null closes the frame at its bottom.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0 or no number.</exception>
    public double? BordersContinueBelow
    {
        get => _bordersContinueBelow;
        init => _bordersContinueBelow = Reach(value);
    }

    /// <summary>The color it is shaded with, as Word's: from margin to margin down its lines, or all inside its lines
    /// where it has borders. The room down to a next paragraph between the same margins, shaded too, takes this color,
    /// whatever that one's. Null shades nothing. Horizontal text only.</summary>
    public Color? Shading { get => _shading; init => _shading = value; }

    /// <summary>For the layout's last paragraph, laid out to its end: its shading goes on this far below its last line,
    /// through the room down to a shaded paragraph laid out elsewhere below between the same margins, as the room
    /// between two paragraphs in one layout is shaded - drawn with it, under its text. Null stops it at the last line;
    /// a line below closing its frame stops it there too.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0 or no number.</exception>
    public double? ShadingContinuesBelow
    {
        get => _shadingContinuesBelow;
        init => _shadingContinuesBelow = Reach(value);
    }

    /// <summary>The room before its first line, at the side its lines start, that a mark drawn apart from the text - a
    /// list's bullet or number - takes. Its borders and shading go round it, as round a first line hung out by a negative
    /// <see cref="TextIndent"/>, as Word's do: from whichever starts further out.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 0 or no number.</exception>
    public double MarkRoom
    {
        get => _markRoom ?? 0;
        init => _markRoom = Reach(value);
    }

    /// <summary>Whether its shading goes on down to <paramref name="next"/>, the paragraph after it: both are shaded,
    /// between the same margins.</summary>
    public bool ShadesOnTo(ParagraphAttributes next) =>
        _shading != null && next?._shading != null && MarginLeft.Equals(next.MarginLeft) &&
        MarginRight.Equals(next.MarginRight);

    private static double? Reach(double? value) =>
        value is not { } reach || (double.IsFinite(reach) && reach >= 0)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A frame goes on 0 or more.");

    /// <summary>Whether it draws a line on any side.</summary>
    public bool HasBorders => _borderTop is { IsVisible: true } || _borderBottom is { IsVisible: true } ||
                              _borderLeft is { IsVisible: true } || _borderRight is { IsVisible: true };

    /// <summary>Whether it and <paramref name="other"/>, one after the other, share one frame: both draw lines, the
    /// same ones, between the same margins.</summary>
    public bool SharesBordersWith(ParagraphAttributes other) =>
        other != null && HasBorders && SameLine(_borderTop, other._borderTop) &&
        SameLine(_borderBottom, other._borderBottom) && SameLine(_borderLeft, other._borderLeft) &&
        SameLine(_borderRight, other._borderRight) && MarginLeft.Equals(other.MarginLeft) &&
        MarginRight.Equals(other.MarginRight);

    private static bool SameLine(ParagraphBorder one, ParagraphBorder other) =>
        one is { IsVisible: true } ? one.Equals(other) : other is not { IsVisible: true };

    /// <summary>Whether the paragraphs stand alike: the fields' values, whether set or not.</summary>
    public bool Equals(ParagraphAttributes other) =>
        other != null && Alignment == other.Alignment && MarginLeft.Equals(other.MarginLeft)
        && MarginRight.Equals(other.MarginRight) && SpaceBefore.Equals(other.SpaceBefore)
        && SpaceAfter.Equals(other.SpaceAfter) && TextIndent.Equals(other.TextIndent)
        && LineHeight.Equals(other.LineHeight) && Equals(_borderTop, other._borderTop)
        && Equals(_borderBottom, other._borderBottom) && Equals(_borderLeft, other._borderLeft)
        && Equals(_borderRight, other._borderRight) && _bordersContinueAbove == other._bordersContinueAbove
        && _bordersContinueBelow == other._bordersContinueBelow && _shading == other._shading
        && _shadingContinuesBelow == other._shadingContinuesBelow && MarkRoom.Equals(other.MarkRoom);

    public override int GetHashCode() =>
        HashCode.Combine(HashCode.Combine(Alignment, MarginLeft, MarginRight, SpaceBefore, SpaceAfter, TextIndent,
            LineHeight), _borderTop, _borderBottom, _borderLeft, _borderRight, _bordersContinueAbove,
            _bordersContinueBelow, HashCode.Combine(_shading, _shadingContinuesBelow, MarkRoom));

    /// <summary>These attributes with every field <paramref name="over"/> sets taken from it.</summary>
    public ParagraphAttributes With(ParagraphAttributes over)
    {
        if (over == null)
        {
            return this;
        }

        return new ParagraphAttributes
        {
            _alignment = over._alignment ?? _alignment,
            _marginLeft = over._marginLeft ?? _marginLeft,
            _marginRight = over._marginRight ?? _marginRight,
            _spaceBefore = over._spaceBefore ?? _spaceBefore,
            _spaceAfter = over._spaceAfter ?? _spaceAfter,
            _textIndent = over._textIndent ?? _textIndent,
            _lineHeight = over._lineHeight ?? _lineHeight,
            _borderTop = over._borderTop ?? _borderTop,
            _borderBottom = over._borderBottom ?? _borderBottom,
            _borderLeft = over._borderLeft ?? _borderLeft,
            _borderRight = over._borderRight ?? _borderRight,
            _bordersContinueAbove = over._bordersContinueAbove ?? _bordersContinueAbove,
            _bordersContinueBelow = over._bordersContinueBelow ?? _bordersContinueBelow,
            _shading = over._shading ?? _shading,
            _shadingContinuesBelow = over._shadingContinuesBelow ?? _shadingContinuesBelow,
            _markRoom = over._markRoom ?? _markRoom,
        };
    }
}
