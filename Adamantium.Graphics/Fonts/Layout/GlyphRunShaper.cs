using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Fonts.Text;
using static Adamantium.Graphics.Fonts.Layout.TextGeometry;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class GlyphRunShaper
{
    private static readonly char[] LineEnds = ['\n', LineSeparator];

    private static readonly char[] LineEndsAndObjects = ['\n', LineSeparator, ObjectReplacement];

    private readonly IFont _font;
    private readonly Dictionary<(IFont Font, int Codepoint), bool> _verticalForms = new();
    private bool _pending;

    public GlyphRunShaper(IFont font)
    {
        _font = font;
    }

    public List<ShapedItem> Shape(string text, AttributedText attributed, double fontSize, int split, bool vertical,
        TextLayoutOptions options, TextParagraphs paragraphs, bool[] graphemes, out bool hasPendingFonts)
    {
        _pending = false;
        var items = new List<ShapedItem>(text.Length);
        var runs = attributed?.Runs;
        var runIndex = 0;
        foreach (var (start, end, attributes) in SplitAt(ShapingSegments(text, attributed, fontSize), split))
        {
            var language = attributes?.Language ?? options.Language;
            var shapingOptions = attributes == null && language == null
                ? ShapingOptions.Default
                : new ShapingOptions(null, language, attributes?.Features);
            var font = RunFont(_font, attributes, fontSize);
            var position = start;
            var stops = attributes?.ObjectSize != null ? LineEndsAndObjects : LineEnds;
            while (position < end)
            {
                var newline = text.IndexOfAny(stops, position, end - position);
                var stop = newline < 0 ? end : newline;
                foreach (var (runStart, runEnd, runFont, pending) in FontRuns(text, graphemes, position, stop, font, language,
                             options))
                {
                    var blank = pending ? runFont.GetGlyphByCharacter(' ') : null;
                    foreach (var (levelStart, levelEnd, direction) in TextParagraphs.LevelRuns(paragraphs, runStart, runEnd))
                    {
                        if (!vertical)
                        {
                            AddAcross(shapingOptions, runFont, blank, levelStart, levelEnd, direction);
                            continue;
                        }

                        foreach (var (orientedStart, orientedEnd, upright, combined) in
                                 OrientationRuns(text, levelStart, levelEnd, codepoint => StandsUpright(runFont, codepoint)))
                        {
                            if (combined)
                            {
                                AddCombined(shapingOptions, runFont, blank, orientedStart, orientedEnd);
                            }
                            else if (upright)
                            {
                                AddUpright(shapingOptions, runFont, blank, orientedStart, orientedEnd);
                            }
                            else
                            {
                                AddAcross(shapingOptions, runFont, blank, orientedStart, orientedEnd, direction);
                            }
                        }
                    }
                }

                if (newline < 0)
                {
                    break;
                }

                var newlineAttributes = AttributesAt(newline);
                if (text[newline] == ObjectReplacement)
                {
                    items.Add(new ShapedItem(font.GetGlyphByCharacter(' '), ObjectReplacement, newline,
                        newlineAttributes.ObjectSize.Value.Width, 0,
                        Raise(newlineAttributes, font, newlineAttributes.FontSize ?? fontSize, fontSize,
                            newlineAttributes.ObjectSize.Value.Height),
                        newlineAttributes, font, newlineAttributes.FontSize ?? fontSize));
                }
                else
                {
                    items.Add(new ShapedItem(font.GetGlyphByCharacter('\n'), '\n', newline, 0, 0, 0, newlineAttributes,
                        font, newlineAttributes?.FontSize ?? fontSize));
                }

                position = newline + 1;
            }
        }

        hasPendingFonts = _pending;
        return items;

        double Raise(TextAttributes raised, IFont raisedFont, double size, double baseSize,
            double objectHeight = double.NaN) =>
            TextGeometry.Raise(_font, options.WritingMode, raised, raisedFont, size, baseSize, objectHeight);

        void AddAcross(ShapingOptions shaping, IFont runFont, Glyph blank, int start, int end, TextDirection direction)
        {
            var shaped = direction == TextDirection.Auto
                ? TextShaper.Shape(runFont, text, start, end, shaping)
                : TextShaper.Shape(runFont, text, start, end,
                    new ShapingOptions(shaping.Script, shaping.Language, shaping.Features, direction));
            if (direction == TextDirection.RightToLeft)
            {
                shaped = ToLogicalOrder(shaped);
            }

            for (var k = 0; k < shaped.Length; k++)
            {
                var glyph = shaped[k];
                var cluster = start + glyph.Cluster;
                var glyphAttributes = AttributesAt(cluster);
                var size = glyphAttributes?.FontSize ?? fontSize;
                var scale = size / runFont.UnitsPerEm;
                var advance = glyph.XAdvance * scale;
                if (advance > 0 && IsEmboldened(glyphAttributes))
                {
                    advance += 2 * FontSynthesisRules.EmboldenPerSide(size) * size;
                }

                var carries = direction == TextDirection.RightToLeft
                    ? k == 0 || shaped[k - 1].Cluster != glyph.Cluster
                    : k == shaped.Length - 1 || shaped[k + 1].Cluster != glyph.Cluster;
                var widthScale = IsBlank(text[cluster]) ? 1 : options.GlyphScaling.Desired;
                items.Add(new ShapedItem(blank ?? runFont.GetGlyphByIndex(glyph.GlyphIndex), text[cluster],
                    cluster, Spaced(advance * widthScale, runFont, size, glyphAttributes, cluster, carries),
                    glyph.XOffset * scale * widthScale,
                    glyph.YOffset * scale + Raise(glyphAttributes, runFont, size, fontSize),
                    glyphAttributes, runFont, size,
                    horizontalScale: widthScale));
            }
        }

        double Spaced(double advance, IFont font, double size, TextAttributes attributes, int cluster, bool carries)
        {
            var symbol = text[cluster];
            if (symbol == ' ')
            {
                advance *= options.WordSpacing.Desired;
            }

            var tracking = attributes?.Tracking ?? options.Tracking;
            if (!carries || (tracking == 0 && options.LetterSpacing.Desired == 0) || symbol is '\t' or '\n'
                || CursiveScripts.Contains(CodepointAt(text, cluster)))
            {
                return advance;
            }

            var spaced = advance + tracking * size / 1000 + options.LetterSpacing.Desired * SpaceWidth(font, size);
            return advance > 0 ? Math.Max(0, spaced) : spaced;
        }

        void AddUpright(ShapingOptions shaping, IFont runFont, Glyph blank, int start, int end)
        {
            var shaped = TextShaper.Shape(runFont, text, start, end,
                new ShapingOptions(shaping.Script, shaping.Language, shaping.Features, vertical: true));
            for (var k = 0; k < shaped.Length; k++)
            {
                var glyph = shaped[k];
                var cluster = start + glyph.Cluster;
                var glyphAttributes = AttributesAt(cluster);
                var size = glyphAttributes?.FontSize ?? fontSize;
                var scale = size / runFont.UnitsPerEm;
                var carries = k == shaped.Length - 1 || shaped[k + 1].Cluster != glyph.Cluster;
                items.Add(new ShapedItem(blank ?? runFont.GetGlyphByIndex(glyph.GlyphIndex), text[cluster],
                    cluster, Spaced(-glyph.YAdvance * scale, runFont, size, glyphAttributes, cluster, carries),
                    -glyph.YOffset * scale, glyph.XOffset * scale + Raise(glyphAttributes, runFont, size, fontSize),
                    glyphAttributes,
                    runFont, size, true));
            }
        }

        void AddCombined(ShapingOptions shaping, IFont runFont, Glyph blank, int start, int end)
        {
            var shaped = TextShaper.Shape(runFont, text, start, end, shaping);
            var size = AttributesAt(start)?.FontSize ?? fontSize;
            var width = shaped.Sum(glyph => glyph.XAdvance) * size / runFont.UnitsPerEm;
            var fitted = width > size ? size * size / width : size;
            var scale = fitted / runFont.UnitsPerEm;
            var baseline = (size + (runFont.LineAscent - runFont.LineDescent) * scale) / 2;
            double pen = -Math.Min(width, size) / 2;
            for (var k = 0; k < shaped.Length; k++)
            {
                var glyph = shaped[k];
                var cluster = start + glyph.Cluster;
                var glyphAttributes = AttributesAt(cluster);
                items.Add(new ShapedItem(blank ?? runFont.GetGlyphByIndex(glyph.GlyphIndex), text[cluster], cluster,
                    k == 0 ? size : 0, baseline - glyph.YOffset * scale - (k == 0 ? 0 : size),
                    pen + glyph.XOffset * scale + Raise(glyphAttributes, runFont, fitted, fontSize), glyphAttributes,
                    runFont, fitted,
                    true));
                pen += glyph.XAdvance * scale;
            }
        }

        TextAttributes AttributesAt(int index)
        {
            if (runs == null)
            {
                return null;
            }

            while (runIndex < runs.Count - 1 && index >= runs[runIndex].End)
            {
                runIndex++;
            }

            return runs[runIndex].Attributes;
        }
    }

    private static ShapedGlyph[] ToLogicalOrder(ShapedGlyph[] visual)
    {
        var logical = new ShapedGlyph[visual.Length];
        var start = 0;
        while (start < visual.Length)
        {
            var end = start + 1;
            while (end < visual.Length && visual[end].Cluster <= visual[end - 1].Cluster)
            {
                end++;
            }

            for (var k = start; k < end; k++)
            {
                logical[start + end - 1 - k] = visual[k];
            }

            start = end;
        }

        return logical;
    }

    private static IEnumerable<(int Start, int End, TextAttributes Attributes)> SplitAt(
        IEnumerable<(int Start, int End, TextAttributes Attributes)> segments, int split)
    {
        foreach (var segment in segments)
        {
            if (split > segment.Start && split < segment.End)
            {
                yield return (segment.Start, split, segment.Attributes);
                yield return (split, segment.End, segment.Attributes);
                continue;
            }

            yield return segment;
        }
    }

    private IEnumerable<(int Start, int End, TextAttributes Attributes)> ShapingSegments(string text,
        AttributedText attributed, double fontSize)
    {
        if (attributed == null || attributed.Runs.Count == 0)
        {
            yield return (0, text.Length, null);
            yield break;
        }

        var runs = attributed.Runs;
        var start = runs[0].Start;
        var attributes = runs[0].Attributes;
        for (var i = 1; i < runs.Count; i++)
        {
            if (runs[i].Attributes.ShapesLike(attributes)
                && ReferenceEquals(RunFont(_font, runs[i].Attributes, fontSize), RunFont(_font, attributes, fontSize)))
            {
                continue;
            }

            yield return (start, runs[i].Start, attributes);
            start = runs[i].Start;
            attributes = runs[i].Attributes;
        }

        yield return (start, text.Length, attributes);
    }

    private IEnumerable<(int Start, int End, IFont Font, bool Pending)> FontRuns(string text, bool[] graphemes, int start,
        int end, IFont font, string language, TextLayoutOptions options)
    {
        if (end <= start)
        {
            yield break;
        }

        if (options.Fallback == null)
        {
            yield return (start, end, font, false);
            yield break;
        }

        var runStart = start;
        IFont runFont = null;
        var runPending = false;
        var index = start;
        while (index < end)
        {
            var codepoint = char.IsHighSurrogate(text[index]) && index + 1 < end && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1])
                : text[index];
            var nextGrapheme = TextBoundaries.Next(graphemes, index);
            var graphemeEnd = Math.Min(nextGrapheme, end);
            IFont chosen;
            var pending = false;
            if (runFont != null && (JoinsPrevious(codepoint) || IsSpaceOrControl(codepoint)))
            {
                chosen = runFont;
                pending = runPending;
            }
            else if (IsSpaceOrControl(codepoint))
            {
                chosen = font;
            }
            else
            {
                var emoji = EmojiPresentation.IsEmoji(text, index, nextGrapheme);
                if (emoji ? HasColorGlyph(font, codepoint) : font.TryGetGlyphIndex(codepoint, out _))
                {
                    chosen = font;
                }
                else if (options.LoadFontsInBackground)
                {
                    chosen = options.Fallback.FontFor(codepoint, font, language, emoji, out pending) ?? font;
                    _pending |= pending;
                }
                else
                {
                    chosen = options.Fallback.FontFor(codepoint, font, language, emoji) ?? font;
                }
            }

            if (runFont == null)
            {
                runFont = chosen;
                runPending = pending;
            }
            else if (!ReferenceEquals(chosen, runFont) || pending != runPending)
            {
                yield return (runStart, index, runFont, runPending);
                runStart = index;
                runFont = chosen;
                runPending = pending;
            }

            index = graphemeEnd;
        }

        yield return (runStart, end, runFont, runPending);
    }

    private static bool HasColorGlyph(IFont font, int codepoint)
    {
        if (!font.TryGetGlyphIndex(codepoint, 0xFE0F, out var glyph) && !font.TryGetGlyphIndex(codepoint, out glyph))
        {
            return false;
        }

        return font.GetColorPaint(glyph).Count > 0 || font.GetColorLayers(glyph).Count > 0 ||
               (font.ColorBitmapSizes.Count > 0 && font.GetColorBitmap(glyph, 0) != null);
    }

    private static bool JoinsPrevious(int codepoint)
    {
        if (codepoint is 0x200C or 0x200D or >= 0xFE00 and <= 0xFE0F or >= 0x1F3FB and <= 0x1F3FF
            or >= 0xE0020 and <= 0xE007F or >= 0xE0100 and <= 0xE01EF)
        {
            return true;
        }

        return CharUnicodeInfo.GetUnicodeCategory(codepoint) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private bool StandsUpright(IFont font, int codepoint)
    {
        var orientation = VerticalOrientations.Of(codepoint);
        if (orientation != VerticalOrientation.TransformedOrRotated)
        {
            return orientation != VerticalOrientation.Rotated;
        }

        if (!_verticalForms.TryGetValue((font, codepoint), out var has))
        {
            var character = char.ConvertFromUtf32(codepoint);
            var down = TextShaper.Shape(font, character, new ShapingOptions(vertical: true));
            var across = TextShaper.Shape(font, character, ShapingOptions.Default);
            has = down.Length == 1 && across.Length == 1 && down[0].GlyphIndex != across[0].GlyphIndex;
            _verticalForms[(font, codepoint)] = has;
        }

        return has;
    }

    private static IEnumerable<(int Start, int End, bool Upright, bool Combined)> OrientationRuns(string text, int start,
        int end, Func<int, bool> standsUpright)
    {
        var runStart = start;
        var upright = false;
        var combined = false;
        var index = start;
        while (index < end)
        {
            var codepoint = char.IsHighSurrogate(text[index]) && index + 1 < end && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1])
                : text[index];
            var next = index + (codepoint > 0xFFFF ? 2 : 1);
            var digits = CombinedDigits(text, index, end);
            if (index > start && digits == 0 && JoinsPrevious(codepoint))
            {
                index = next;
                continue;
            }

            var isCombined = digits > 0;
            var isUpright = isCombined || standsUpright(codepoint);
            if (index > runStart && (isUpright != upright || isCombined || combined))
            {
                yield return (runStart, index, upright, combined);
                runStart = index;
            }

            upright = isUpright;
            combined = isCombined;
            index = isCombined ? index + digits : next;
        }

        if (end > runStart)
        {
            yield return (runStart, end, upright, combined);
        }
    }

    private static int CombinedDigits(string text, int index, int end)
    {
        if (!IsAsciiDigit(text[index]) || (index > 0 && IsAsciiDigit(text[index - 1])))
        {
            return 0;
        }

        var stop = index;
        while (stop < text.Length && IsAsciiDigit(text[stop]))
        {
            stop++;
        }

        if (stop - index > 2 || stop > end || !BesideVerticalText(text, index - 1, -1) || !BesideVerticalText(text, stop, 1))
        {
            return 0;
        }

        return stop - index;
    }

    private static bool BesideVerticalText(string text, int index, int step)
    {
        while (index >= 0 && index < text.Length && text[index] == ' ')
        {
            index += step;
        }

        if (index < 0 || index >= text.Length || text[index] is '\n' or LineSeparator)
        {
            return true;
        }

        int codepoint = text[index];
        if (step < 0 && char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1]))
        {
            codepoint = char.ConvertToUtf32(text[index - 1], text[index]);
        }
        else if (step > 0 && char.IsHighSurrogate(text[index]) && index + 1 < text.Length
                 && char.IsLowSurrogate(text[index + 1]))
        {
            codepoint = char.ConvertToUtf32(text[index], text[index + 1]);
        }

        return VerticalOrientations.Of(codepoint) != VerticalOrientation.Rotated;
    }

    private static bool IsAsciiDigit(char symbol) => symbol is >= '0' and <= '9';
}
