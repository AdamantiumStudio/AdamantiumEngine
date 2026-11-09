using System.Collections.Generic;

namespace Adamantium.Fonts.Encodings;

/// <summary>Adobe's StandardEncoding: the glyph name each code stands for in a Type 1 or CFF font that says nothing else
/// (ISO 32000-2, annex D). Codes without a glyph are null.</summary>
public static class StandardEncoding
{
    private static readonly string[] Names = Build();

    public static IReadOnlyList<string> GlyphNames => Names;

    public static string NameOf(int code) => code is >= 0 and < 256 ? Names[code] : null;

    private static string[] Build()
    {
        var names = new string[256];
        string[] printable =
        [
            "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quoteright", "parenleft",
            "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash", "zero", "one", "two", "three",
            "four", "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater",
            "question", "at", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q",
            "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "bracketleft", "backslash", "bracketright", "asciicircum",
            "underscore", "quoteleft", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
            "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "braceleft", "bar", "braceright", "asciitilde",
        ];
        for (var i = 0; i < printable.Length; i++)
        {
            names[32 + i] = printable[i];
        }

        (int Code, string Name)[] high =
        [
            (161, "exclamdown"), (162, "cent"), (163, "sterling"), (164, "fraction"), (165, "yen"), (166, "florin"),
            (167, "section"), (168, "currency"), (169, "quotesingle"), (170, "quotedblleft"), (171, "guillemotleft"),
            (172, "guilsinglleft"), (173, "guilsinglright"), (174, "fi"), (175, "fl"), (177, "endash"),
            (178, "dagger"), (179, "daggerdbl"), (180, "periodcentered"), (182, "paragraph"), (183, "bullet"),
            (184, "quotesinglbase"), (185, "quotedblbase"), (186, "quotedblright"), (187, "guillemotright"),
            (188, "ellipsis"), (189, "perthousand"), (191, "questiondown"), (193, "grave"), (194, "acute"),
            (195, "circumflex"), (196, "tilde"), (197, "macron"), (198, "breve"), (199, "dotaccent"),
            (200, "dieresis"), (202, "ring"), (203, "cedilla"), (205, "hungarumlaut"), (206, "ogonek"),
            (207, "caron"), (208, "emdash"), (225, "AE"), (227, "ordfeminine"), (232, "Lslash"), (233, "Oslash"),
            (234, "OE"), (235, "ordmasculine"), (241, "ae"), (245, "dotlessi"), (248, "lslash"), (249, "oslash"),
            (250, "oe"), (251, "germandbls"),
        ];
        foreach (var (code, name) in high)
        {
            names[code] = name;
        }

        return names;
    }
}
