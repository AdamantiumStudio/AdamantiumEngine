using System;
using Adamantium.Fonts;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>A composite font (ISO 32000-2, 9.7): a CMap splits strings into codes of one to four bytes and maps them
/// to CIDs, and its CIDFont gives each CID a width and a glyph - a CFF program's by its charset (CIDFontType0), a
/// TrueType program's through <c>/CIDToGIDMap</c> (CIDFontType2).</summary>
public sealed class PdfType0Font : PdfFont
{
    private const double DefaultWidth = 1000;
    private const double DefaultVerticalOrigin = 880;
    private const double DefaultVerticalAdvance = -1000;
    private const string AdobeRegistry = "Adobe";

    private readonly PdfCidMetrics _widths;
    private readonly PdfCidMetrics _verticalMetrics;
    private readonly double _defaultWidth = DefaultWidth;
    private readonly double _defaultVerticalAdvance = DefaultVerticalAdvance;
    private readonly double _defaultVerticalOrigin = DefaultVerticalOrigin;
    private readonly byte[] _cidToGid;
    private readonly IFont _substitute;
    private readonly bool _unicodeCodes;

    internal PdfType0Font(PdfDictionary dictionary, PdfDocument document) : base(dictionary, document)
    {
        Encoding = PdfCMapReader.Read(dictionary[PdfFontKeys.Encoding], document);
        _unicodeCodes = PdfPredefinedCMaps.IsUnicode(Encoding?.Name);
        if (Encoding?.Registry == AdobeRegistry)
        {
            Ordering = Encoding.Ordering;
        }

        var descendants = document.Resolve(dictionary[PdfFontKeys.DescendantFonts]) as PdfArray;
        var cidFont = descendants is { Count: > 0 } ? document.Resolve(descendants[0]) as PdfDictionary : null;
        if (cidFont == null)
        {
            _widths = new PdfCidMetrics(null, document, 1);
            _verticalMetrics = new PdfCidMetrics(null, document, 3);
            return;
        }

        CidFontType = PdfValues.Name(cidFont[PdfFontKeys.Subtype], document);
        if (Ordering == null && document.Resolve(cidFont[PdfFontKeys.CIDSystemInfo]) is PdfDictionary system &&
            (document.Resolve(system[PdfFontKeys.Registry]) as PdfString)?.ToText() == AdobeRegistry)
        {
            Ordering = (document.Resolve(system[PdfFontKeys.Ordering]) as PdfString)?.ToText();
        }

        if (document.Resolve(cidFont[PdfFontKeys.FontDescriptor]) is PdfDictionary descriptor)
        {
            Descriptor = new PdfFontDescriptor(descriptor, document);
        }

        _defaultWidth = PdfValues.Number(cidFont[PdfFontKeys.DW], document, DefaultWidth);
        _widths = new PdfCidMetrics(cidFont[PdfFontKeys.W], document, 1);
        var verticalDefaults = PdfValues.Numbers(cidFont[PdfFontKeys.DW2], document);
        if (verticalDefaults is { Length: 2 })
        {
            _defaultVerticalOrigin = verticalDefaults[0];
            _defaultVerticalAdvance = verticalDefaults[1];
        }

        _verticalMetrics = new PdfCidMetrics(cidFont[PdfFontKeys.W2], document, 3);
        if (document.Resolve(cidFont[PdfFontKeys.CIDToGIDMap]) is PdfStream map)
        {
            try
            {
                _cidToGid = document.Decode(map).Data.ToArray();
            }
            catch (PdfException)
            {
            }
        }

        Typeface = PdfFontPrograms.Embedded(Descriptor, document);
        IsEmbedded = Typeface != null;
        if (!IsEmbedded)
        {
            _substitute = PdfFontPrograms.Substitute(BaseFont, Descriptor);
            Typeface = _substitute?.Typeface;
            Substitute = _substitute?.FullName;
        }
    }

    /// <summary>The CMap from codes to CIDs; null when the font names no predefined CMap or its stream cannot be read.
    /// </summary>
    public PdfCMap Encoding { get; }

    /// <summary>CIDFontType0 or CIDFontType2.</summary>
    public string CidFontType { get; }

    /// <summary>The Adobe character collection its CIDs are of - Japan1, GB1, CNS1, Korea1 - by its CMap's
    /// <c>/CIDSystemInfo</c>, else its CIDFont's (ISO 32000-2, 9.10.2); null for another registry.</summary>
    public string Ordering { get; }

    public override bool IsVertical => Encoding?.IsVertical ?? false;

    public override PdfCharacter ReadCharacter(ReadOnlySpan<byte> text, int position)
    {
        if (position >= text.Length)
        {
            return default;
        }

        if (Encoding == null)
        {
            return position + 1 < text.Length
                ? new PdfCharacter((uint)(text[position] << 8 | text[position + 1]), 2)
                : new PdfCharacter(text[position], 1);
        }

        var length = Encoding.ReadCode(text, position, out var code);
        return new PdfCharacter(code, length);
    }

    /// <summary>The CID of a character, by the CMap; 0 when it maps to none.</summary>
    public uint CidOf(PdfCharacter character) =>
        Encoding?.CidOf(character.Code, character.Length) ?? character.Code;

    public override double WidthOf(PdfCharacter character)
    {
        var cid = CidOf(character);
        if (IsVertical)
        {
            var metrics = _verticalMetrics.Of(cid);
            return -(metrics.Length == 3 ? metrics[0] : _defaultVerticalAdvance) / 1000;
        }

        var width = _widths.Of(cid);
        return (width.Length == 1 ? width[0] : _defaultWidth) / 1000;
    }

    /// <summary>Where a vertical glyph's origin lies from its horizontal one, in text space at size 1 (ISO 32000-2,
    /// 9.7.4.3).</summary>
    public (double X, double Y) VerticalOriginOf(PdfCharacter character)
    {
        var cid = CidOf(character);
        var metrics = _verticalMetrics.Of(cid);
        if (metrics.Length == 3)
        {
            return (metrics[1] / 1000, metrics[2] / 1000);
        }

        var width = _widths.Of(cid);
        return ((width.Length == 1 ? width[0] : _defaultWidth) / 2000, _defaultVerticalOrigin / 1000);
    }

    public override uint GlyphOf(PdfCharacter character)
    {
        if (Typeface == null)
        {
            return 0;
        }

        var cid = CidOf(character);
        if (_substitute != null)
        {
            return PdfFontGlyphs.OfText(_substitute, TextOf(character));
        }

        if (CidFontType == PdfFontTypes.CIDFontType2)
        {
            if (_cidToGid == null)
            {
                return cid;
            }

            var at = (long)cid * 2;
            return at + 1 < _cidToGid.Length ? (uint)(_cidToGid[at] << 8 | _cidToGid[at + 1]) : 0;
        }

        return Typeface.GetGlyphByCid(cid, out var glyph) ? glyph.Index : 0;
    }

    private protected override string EncodedText(PdfCharacter character)
    {
        if (_unicodeCodes && PdfCodeText.Utf16(character.Code, character.Length) is { } text &&
            !(text.Length == 1 && char.IsControl(text[0])))
        {
            return text;
        }

        return Ordering != null ? PdfCidTexts.Of(Ordering).TextOf(CidOf(character)) : null;
    }
}
