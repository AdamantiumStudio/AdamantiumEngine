using System;

namespace Adamantium.Graphics.Fonts;

/// <summary>A named character style, as InDesign's: attributes a range of text takes, over its paragraph's style and
/// under what the range sets itself. A style <see cref="BasedOn"/> another takes what that one sets and sets over it.
/// Changing a style changes every text it is set on the next time that text is laid out.</summary>
public sealed class CharacterStyle
{
    private TextAttributes _attributes;
    private CharacterStyle _basedOn;

    public CharacterStyle(string name, TextAttributes attributes = null, CharacterStyle basedOn = null)
    {
        Name = name;
        _attributes = attributes;
        BasedOn = basedOn;
    }

    public string Name { get; }

    /// <summary>What the style sets; null sets nothing.</summary>
    public TextAttributes Attributes
    {
        get => _attributes;
        set
        {
            _attributes = value;
            Version++;
        }
    }

    /// <summary>The style this one starts from; never one based on this.</summary>
    public CharacterStyle BasedOn
    {
        get => _basedOn;
        set
        {
            for (var style = value; style != null; style = style._basedOn)
            {
                if (ReferenceEquals(style, this))
                {
                    throw new ArgumentException("A style cannot be based on itself.", nameof(value));
                }
            }

            _basedOn = value;
            Version++;
        }
    }

    internal int Version { get; private set; }

    /// <summary>What the style and those it is based on set, this one over them.</summary>
    public TextAttributes Resolved => _basedOn == null
        ? _attributes ?? TextAttributes.Empty
        : _basedOn.Resolved.With(_attributes);

    // Changes whenever this style or one it is based on changes.
    internal long Stamp()
    {
        long stamp = 0;
        for (var style = this; style != null; style = style._basedOn)
        {
            stamp = stamp * 31 + style.Version + 1;
        }

        return stamp;
    }
}

/// <summary>A named paragraph style, as InDesign's: how a paragraph stands and the attributes its text takes, under its
/// character styles and what its ranges set themselves; the paragraph's own <see cref="ParagraphAttributes"/> set over
/// it. A style <see cref="BasedOn"/> another takes what that one sets and sets over it. Changing a style changes every
/// paragraph it is set on the next time that text is laid out.</summary>
public sealed class ParagraphStyle
{
    private ParagraphAttributes _paragraph;
    private TextAttributes _text;
    private ParagraphStyle _basedOn;

    public ParagraphStyle(string name, ParagraphAttributes paragraph = null, TextAttributes text = null,
        ParagraphStyle basedOn = null)
    {
        Name = name;
        _paragraph = paragraph;
        _text = text;
        BasedOn = basedOn;
    }

    public string Name { get; }

    /// <summary>How the paragraph stands; null sets nothing.</summary>
    public ParagraphAttributes Paragraph
    {
        get => _paragraph;
        set
        {
            _paragraph = value;
            Version++;
        }
    }

    /// <summary>The attributes the paragraph's text takes; null sets nothing.</summary>
    public TextAttributes Text
    {
        get => _text;
        set
        {
            _text = value;
            Version++;
        }
    }

    /// <summary>The style this one starts from; never one based on this.</summary>
    public ParagraphStyle BasedOn
    {
        get => _basedOn;
        set
        {
            for (var style = value; style != null; style = style._basedOn)
            {
                if (ReferenceEquals(style, this))
                {
                    throw new ArgumentException("A style cannot be based on itself.", nameof(value));
                }
            }

            _basedOn = value;
            Version++;
        }
    }

    internal int Version { get; private set; }

    /// <summary>How the paragraph stands by this style and those it is based on, this one over them.</summary>
    public ParagraphAttributes ResolvedParagraph => _basedOn == null
        ? _paragraph ?? new ParagraphAttributes()
        : _basedOn.ResolvedParagraph.With(_paragraph);

    /// <summary>What the paragraph's text takes by this style and those it is based on, this one over them.</summary>
    public TextAttributes ResolvedText => _basedOn == null
        ? _text ?? TextAttributes.Empty
        : _basedOn.ResolvedText.With(_text);

    // Changes whenever this style or one it is based on changes.
    internal long Stamp()
    {
        long stamp = 0;
        for (var style = this; style != null; style = style._basedOn)
        {
            stamp = stamp * 31 + style.Version + 1;
        }

        return stamp;
    }
}
