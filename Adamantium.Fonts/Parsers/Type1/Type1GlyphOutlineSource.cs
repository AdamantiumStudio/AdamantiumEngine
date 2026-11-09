using System.Collections.Generic;
using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>Draws a Type 1 glyph from its charstring when its outline is first asked for.</summary>
internal sealed class Type1GlyphOutlineSource : IGlyphOutlineSource
{
    private readonly IReadOnlyDictionary<int, byte[]> _subrs;
    private readonly IReadOnlyDictionary<string, byte[]> _charStrings;

    public Type1GlyphOutlineSource(IReadOnlyDictionary<int, byte[]> subrs,
        IReadOnlyDictionary<string, byte[]> charStrings, long dataLength)
    {
        _subrs = subrs;
        _charStrings = charStrings;
        Budget = FontWorkBudget.ForCharStrings(dataLength, charStrings.Count);
    }

    /// <summary>The charstring steps left to all glyphs of the font.</summary>
    public FontWorkBudget Budget { get; }

    public byte[] CharStringOf(string name) =>
        name != null && _charStrings.TryGetValue(name, out var code) ? code : null;

    public void LoadOutlines(Glyph glyph)
    {
        var code = CharStringOf(glyph.Name);
        if (code == null)
        {
            return;
        }

        var interpreter = new Type1CharStringInterpreter(_subrs, CharStringOf, Budget);
        interpreter.Run(code);
        glyph.FillOutlines(interpreter.Commands).RecalculateBounds(true);
    }
}
