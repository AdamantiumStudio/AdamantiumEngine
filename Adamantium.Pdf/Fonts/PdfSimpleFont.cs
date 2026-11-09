using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>A simple font - Type 1, TrueType or Type 3 (ISO 32000-2, 9.6): one byte a code, widths by code, and an
/// encoding that names each code's glyph.</summary>
public class PdfSimpleFont : PdfFont
{
    private const int SymbolCodes = 0xF000;

    private readonly double[] _widths = new double[256];
    private readonly string[] _names = new string[256];
    private readonly bool[] _named = new bool[256];
    private readonly IFont _substitute;
    private readonly bool _symbolic;

    internal PdfSimpleFont(PdfDictionary dictionary, PdfDocument document) : base(dictionary, document)
    {
        if (document.Resolve(dictionary[PdfFontKeys.FontDescriptor]) is PdfDictionary descriptor)
        {
            Descriptor = new PdfFontDescriptor(descriptor, document);
        }

        _symbolic = Descriptor?.IsSymbolic ?? PdfFontNames.BuiltInEncodingOf(BaseFont) != null;
        if (Subtype != PdfFontTypes.Type3)
        {
            Typeface = PdfFontPrograms.Embedded(Descriptor, document);
            IsEmbedded = Typeface != null;
            if (!IsEmbedded)
            {
                _substitute = PdfFontPrograms.Substitute(BaseFont, Descriptor);
                Typeface = _substitute?.Typeface;
                Substitute = _substitute?.FullName;
            }
        }

        ReadEncoding(dictionary, document);
        ReadWidths(dictionary, document);
    }

    public override PdfCharacter ReadCharacter(ReadOnlySpan<byte> text, int position) =>
        position < text.Length ? new PdfCharacter(text[position], 1) : default;

    public override double WidthOf(PdfCharacter character) =>
        character.Code < 256 ? _widths[character.Code] / 1000 : 0;

    /// <summary>The glyph name the encoding gives a code, null when it gives none.</summary>
    public string GlyphNameOf(uint code) => code < 256 ? _names[code] : null;

    public override uint GlyphOf(PdfCharacter character)
    {
        if (Typeface == null || character.Code >= 256)
        {
            return 0;
        }

        var code = character.Code;
        var name = _names[code];
        if (_substitute != null)
        {
            return SubstituteGlyph(code, name);
        }

        return Typeface.BuiltInEncoding != null ? CffOrType1Glyph(code, name) : TrueTypeGlyph(code, name);
    }

    private protected override string EncodedText(PdfCharacter character) =>
        character.Code < 256 ? PdfSimpleEncodings.TextOf(_names[character.Code]) : null;

    private uint CffOrType1Glyph(uint code, string name)
    {
        if (name != null && Typeface.GetGlyphByName(name, out var glyph))
        {
            return glyph.Index;
        }

        return !_named[code] && Typeface.BuiltInEncoding[(int)code] is var builtIn and > 0 ? (uint)builtIn : 0;
    }

    /// <summary>A TrueType program's glyph (ISO 32000-2, 9.6.5.4): a nonsymbolic font's code by the text its name
    /// stands for, through the program's Unicode cmap, else by the name its post table gives; a symbolic font's code
    /// as it is, or in the 0xF000 page a Microsoft symbol cmap puts it.</summary>
    private uint TrueTypeGlyph(uint code, string name) =>
        _symbolic ? SymbolicGlyph(Typeface.CurrentFont, Typeface, code, name)
            : NamedGlyph(Typeface.CurrentFont, Typeface, code, name);

    private uint SubstituteGlyph(uint code, string name) =>
        _symbolic ? SymbolicGlyph(_substitute, _substitute.Typeface, code, name)
            : NamedGlyph(_substitute, _substitute.Typeface, code, name);

    /// <summary>A glyph by the text its name stands for, or by the name itself first when the name is a ligature or
    /// a variant (<c>f_i</c>, <c>a.sc</c>), which a character map cannot tell from the plain letter; a name the program
    /// has no glyph for is the missing glyph, and only a code the encoding names nothing goes to the map as it is.
    /// </summary>
    private static uint NamedGlyph(IFont font, Typeface program, uint code, string name)
    {
        if (name == null)
        {
            return CodeGlyph(font, code);
        }

        var composed = name.Contains('_') || name.Contains('.');
        if (composed && program.GetGlyphByName(name, out var exact))
        {
            return exact.Index;
        }

        if (PdfFontGlyphs.OfText(font, PdfSimpleEncodings.TextOf(name)) is var byText and > 0)
        {
            return byText;
        }

        return program.GetGlyphByName(name, out var byName) ? byName.Index : 0;
    }

    private static uint SymbolicGlyph(IFont font, Typeface program, uint code, string name)
    {
        if (CodeGlyph(font, code) is var byCode and > 0)
        {
            return byCode;
        }

        return name != null && program.GetGlyphByName(name, out var byName) ? byName.Index : 0;
    }

    /// <summary>A code as it is, or in the 0xF000 page where a Microsoft symbol cmap puts it.</summary>
    private static uint CodeGlyph(IFont font, uint code) =>
        PdfFontGlyphs.OfCode(font, code) is var glyph and > 0 ? glyph : PdfFontGlyphs.OfCode(font, SymbolCodes | code);

    private void ReadEncoding(PdfDictionary dictionary, PdfDocument document)
    {
        var encoding = document.Resolve(dictionary[PdfFontKeys.Encoding]);
        var given = encoding switch
        {
            PdfName name => PdfSimpleEncodings.Named(name.Value),
            PdfDictionary differences => PdfSimpleEncodings.Named(
                PdfValues.Name(differences[PdfFontKeys.BaseEncoding], document)),
            _ => null,
        };
        var baseNames = given ?? DefaultEncoding();
        if (baseNames != null)
        {
            for (var code = 0; code < 256; code++)
            {
                _names[code] = baseNames[code];
                _named[code] = given != null && baseNames[code] != null;
            }
        }

        if (encoding is PdfDictionary dictionaryEncoding &&
            document.Resolve(dictionaryEncoding[PdfFontKeys.Differences]) is PdfArray list)
        {
            ApplyDifferences(list, document);
        }
    }

    /// <summary>The encoding a font has when its dictionary names none: a standard symbol font's own, an embedded
    /// program's built-in one, StandardEncoding for a nonsymbolic font, and none for a symbolic TrueType font, whose
    /// codes go straight to its cmap.</summary>
    private IReadOnlyList<string> DefaultEncoding()
    {
        if (!IsEmbedded && PdfFontNames.BuiltInEncodingOf(BaseFont) is { } standardSymbol)
        {
            return standardSymbol;
        }

        if (IsEmbedded && Typeface.BuiltInEncoding is { } builtIn)
        {
            var names = new string[256];
            for (var code = 0; code < 256; code++)
            {
                if (builtIn[code] >= 0 && Typeface.GetGlyphByIndex((uint)builtIn[code], out var glyph) &&
                    !string.IsNullOrEmpty(glyph.Name))
                {
                    names[code] = glyph.Name;
                }
            }

            return names;
        }

        return _symbolic && Subtype != PdfFontTypes.Type1 ? null : PdfSimpleEncodings.Standard;
    }

    private void ApplyDifferences(PdfArray list, PdfDocument document)
    {
        var code = -1;
        foreach (var item in list)
        {
            switch (document.Resolve(item))
            {
                case PdfInteger number:
                    code = number.Value is >= 0 and < 256 ? (int)number.Value : -1;
                    break;
                case PdfName name when code is >= 0 and < 256:
                    _names[code] = name.Value;
                    _named[code] = true;
                    code++;
                    break;
            }
        }
    }

    private void ReadWidths(PdfDictionary dictionary, PdfDocument document)
    {
        var missing = Descriptor?.MissingWidth ?? 0;
        var widths = document.Resolve(dictionary[PdfFontKeys.Widths]) as PdfArray;
        var first = PdfValues.Integer(dictionary[PdfFontKeys.FirstChar], document);
        for (var code = 0; code < 256; code++)
        {
            var at = code - first;
            _widths[code] = widths != null && at >= 0 && at < widths.Count
                ? PdfValues.Number(widths[at], document, missing)
                : widths != null ? missing : ProgramWidth((uint)code, missing);
        }
    }

    /// <summary>A width the file does not give - a standard font's - from the program drawing it.</summary>
    private double ProgramWidth(uint code, double fallback)
    {
        var glyph = GlyphOf(new PdfCharacter(code, 1));
        var font = _substitute ?? Typeface?.CurrentFont;
        if (glyph == 0 || font == null || font.UnitsPerEm == 0 || !Typeface.GetGlyphByIndex(glyph, out var drawn))
        {
            return fallback;
        }

        return drawn.AdvanceWidth * 1000.0 / font.UnitsPerEm;
    }
}
