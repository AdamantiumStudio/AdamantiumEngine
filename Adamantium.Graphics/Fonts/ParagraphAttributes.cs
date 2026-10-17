using System;

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

    /// <summary>Whether the paragraphs stand alike: the fields' values, whether set or not.</summary>
    public bool Equals(ParagraphAttributes other) =>
        other != null && Alignment == other.Alignment && MarginLeft.Equals(other.MarginLeft)
        && MarginRight.Equals(other.MarginRight) && SpaceBefore.Equals(other.SpaceBefore)
        && SpaceAfter.Equals(other.SpaceAfter) && TextIndent.Equals(other.TextIndent)
        && LineHeight.Equals(other.LineHeight);

    public override int GetHashCode() =>
        HashCode.Combine(Alignment, MarginLeft, MarginRight, SpaceBefore, SpaceAfter, TextIndent, LineHeight);

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
        };
    }
}
