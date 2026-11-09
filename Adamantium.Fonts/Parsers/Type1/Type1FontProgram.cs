using System.Collections.Generic;

namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>What a Type 1 font program says: its name, matrix and encoding from the clear text, and its decrypted
/// subroutines and charstrings from the private part.</summary>
internal sealed class Type1FontProgram
{
    public string FontName { get; set; }

    public double[] FontMatrix { get; set; }

    public double[] FontBBox { get; set; }

    /// <summary>The glyph name of each code; null when the font uses StandardEncoding.</summary>
    public string[] Encoding { get; set; }

    public int LenIV { get; set; } = 4;

    public Dictionary<int, byte[]> Subrs { get; } = new();

    /// <summary>The charstrings by glyph name, still encrypted until <see cref="Type1Parser"/> decrypts them.</summary>
    public Dictionary<string, byte[]> CharStrings { get; } = new(System.StringComparer.Ordinal);

    /// <summary>The glyph names in the order the font lists them.</summary>
    public List<string> GlyphNames { get; } = new();
}
