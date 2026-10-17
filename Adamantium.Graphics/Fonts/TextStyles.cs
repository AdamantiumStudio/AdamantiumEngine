using System;
using System.Threading;

namespace Adamantium.Graphics.Fonts;

// Counts every change of any text style, so text that uses none of them checks its own styles only after one.
internal static class TextStyleEdits
{
    private static long _count;

    public static long Count => Interlocked.Read(ref _count);

    public static void Note() => Interlocked.Increment(ref _count);
}

/// <summary>A named character style, as InDesign's: attributes a range of text takes, over its paragraph's style and
/// under what the range sets itself. A style <see cref="BasedOn"/> another takes what that one sets and sets over it.
/// Changing a style changes every text it is set on the next time that text is laid out.</summary>
public sealed class CharacterStyle
{
    private TextAttributes _attributes;
    private CharacterStyle _basedOn;
    private int _version;
    private Tuple<long, TextAttributes> _resolved;

    /// <summary>A style of this name setting these attributes over the style it is based on.</summary>
    public CharacterStyle(string name, TextAttributes attributes = null, CharacterStyle basedOn = null)
    {
        // A style just made is no edit: no text uses it yet, and no chain can come round to it.
        Name = name;
        _attributes = attributes;
        _basedOn = basedOn;
    }

    /// <summary>What the style is called.</summary>
    public string Name { get; }

    /// <summary>What the style sets; null sets nothing.</summary>
    public TextAttributes Attributes
    {
        get => _attributes;
        set
        {
            _attributes = value;
            Changed();
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
            Changed();
        }
    }

    /// <summary>What the style and those it is based on set, this one over them.</summary>
    public TextAttributes Resolved
    {
        get
        {
            var stamp = Stamp();
            if (Volatile.Read(ref _resolved) is { } known && known.Item1 == stamp)
            {
                return known.Item2;
            }

            var attributes = _basedOn == null ? _attributes ?? TextAttributes.Empty : _basedOn.Resolved.With(_attributes);
            Volatile.Write(ref _resolved, Tuple.Create(stamp, attributes));
            return attributes;
        }
    }

    // Changes whenever this style or one it is based on changes.
    internal long Stamp()
    {
        long stamp = 0;
        for (var style = this; style != null; style = style._basedOn)
        {
            stamp = stamp * 31 + Volatile.Read(ref style._version) + 1;
        }

        return stamp;
    }

    private void Changed()
    {
        Interlocked.Increment(ref _version);
        TextStyleEdits.Note();
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
    private int _version;
    private Tuple<long, ParagraphAttributes, TextAttributes> _resolved;

    /// <summary>A style of this name setting how its paragraph stands and what its text takes over the style it is
    /// based on.</summary>
    public ParagraphStyle(string name, ParagraphAttributes paragraph = null, TextAttributes text = null,
        ParagraphStyle basedOn = null)
    {
        Name = name;
        _paragraph = paragraph;
        _text = text;
        _basedOn = basedOn;
    }

    /// <summary>What the style is called.</summary>
    public string Name { get; }

    /// <summary>How the paragraph stands; null sets nothing.</summary>
    public ParagraphAttributes Paragraph
    {
        get => _paragraph;
        set
        {
            _paragraph = value;
            Changed();
        }
    }

    /// <summary>The attributes the paragraph's text takes; null sets nothing.</summary>
    public TextAttributes Text
    {
        get => _text;
        set
        {
            _text = value;
            Changed();
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
            Changed();
        }
    }

    /// <summary>How the paragraph stands by this style and those it is based on, this one over them.</summary>
    public ParagraphAttributes ResolvedParagraph => Resolve().Item2;

    /// <summary>What the paragraph's text takes by this style and those it is based on, this one over them.</summary>
    public TextAttributes ResolvedText => Resolve().Item3;

    private Tuple<long, ParagraphAttributes, TextAttributes> Resolve()
    {
        var stamp = Stamp();
        if (Volatile.Read(ref _resolved) is { } known && known.Item1 == stamp)
        {
            return known;
        }

        var resolved = _basedOn == null
            ? Tuple.Create(stamp, _paragraph ?? new ParagraphAttributes(), _text ?? TextAttributes.Empty)
            : Tuple.Create(stamp, _basedOn.ResolvedParagraph.With(_paragraph), _basedOn.ResolvedText.With(_text));
        Volatile.Write(ref _resolved, resolved);
        return resolved;
    }

    // Changes whenever this style or one it is based on changes.
    internal long Stamp()
    {
        long stamp = 0;
        for (var style = this; style != null; style = style._basedOn)
        {
            stamp = stamp * 31 + Volatile.Read(ref style._version) + 1;
        }

        return stamp;
    }

    private void Changed()
    {
        Interlocked.Increment(ref _version);
        TextStyleEdits.Note();
    }
}
