using System.Collections.Generic;
using Adamantium.Core.Fonts.Shaping;

namespace Adamantium.Fonts.Shaping;

/// <summary>What the text is written in and which OpenType features apply to it.</summary>
public sealed class ShapingOptions
{
    public static readonly ShapingOptions Default = new();

    public ShapingOptions(string script = null, string language = null, IReadOnlyList<FontFeature> features = null,
        TextDirection direction = TextDirection.Auto, bool vertical = false)
    {
        Script = script;
        Language = language;
        Features = features ?? [];
        Direction = direction;
        Vertical = vertical;
    }

    /// <summary>Whether the text runs top to bottom, as HarfBuzz shapes it: the 'vert' forms, no horizontal features
    /// (<c>kern</c>, <c>liga</c>, <c>calt</c>…), advances down by the font's vertical metrics, and offsets from each
    /// glyph's vertical origin. Its glyphs come out in logical order, with negative y advances.</summary>
    public bool Vertical { get; }

    /// <summary>ISO 15924 script code, such as <c>Latn</c> or <c>Cyrl</c>; null takes it from the text.</summary>
    public string Script { get; }

    /// <summary>BCP 47 language tag, such as <c>tr</c> or <c>sr-Cyrl</c>; null uses the font's default.</summary>
    public string Language { get; }

    /// <summary>Features on top of the defaults for the script; a feature set to 0 turns a default off.</summary>
    public IReadOnlyList<FontFeature> Features { get; }

    /// <summary>Which way the text runs; <see cref="TextDirection.Auto"/> takes it from the script, right to left for
    /// Hebrew and Arabic. A right-to-left run comes out in visual order, its first character last.</summary>
    public TextDirection Direction { get; }
}
