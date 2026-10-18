using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.Fonts.Text;

namespace Adamantium.Fonts.Shaping;

/// <summary>Turns text into positioned glyphs of a font, applying its OpenType substitutions and positioning.</summary>
public static class TextShaper
{
    [ThreadStatic]
    private static GlyphBuffer _spareBuffer;

    /// <summary>The features shaping applies to all text without being asked - the required ones (<c>ccmp</c>,
    /// <c>locl</c>, <c>rlig</c>…) and those on by default (<c>calt</c>, <c>liga</c>, <c>kern</c>…); a
    /// <see cref="FontFeature"/> of value 0 turns one off. Any other feature applies only when asked for.</summary>
    public static IReadOnlyList<string> DefaultFeatures { get; } = Array.AsReadOnly(ShapePlan.DefaultFeatures);

    /// <summary>Whether shaping sets a space separator in <paramref name="font"/>: by its own glyph, or by the font's
    /// space at the width the separator asks, as HarfBuzz sets a thin or a narrow no-break space the font lacks.</summary>
    public static bool SetsSpace(IFont font, int codepoint) =>
        font.TryGetGlyphIndex(codepoint, out _)
        || (UnicodeProps.SpaceFallback(codepoint) != SpaceKind.NotSpace && font.TryGetGlyphIndex(0x0020, out _));

    /// <summary>Shapes <paramref name="text"/>; the result is in font design units, each run's glyphs in visual order,
    /// a right-to-left run's first character last. Without a script in <paramref name="options"/> the text is split
    /// where its script changes and each part is shaped with its own.</summary>
    public static ShapedGlyph[] Shape(IFont font, string text, ShapingOptions options = null) =>
        Shape(font, text, 0, text?.Length ?? 0, options);

    /// <summary>Shapes the UTF-16 range from <paramref name="start"/> to <paramref name="end"/> of
    /// <paramref name="text"/> as <see cref="Shape(IFont, string, ShapingOptions)"/> shapes the range alone: its
    /// clusters and the ranges of its features count from <paramref name="start"/>. The text around the range is the
    /// context letters join across, as an Arabic word split between two runs.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the text.</exception>
    public static ShapedGlyph[] Shape(IFont font, string text, int start, int end, ShapingOptions options = null)
    {
        options ??= ShapingOptions.Default;
        var length = text?.Length ?? 0;
        if (start < 0 || start > length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end < start || end > length)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        if (start == end)
        {
            return [];
        }

        if (options.Script != null)
        {
            return ShapeRun(font, text, start, end, start, options.Script, options);
        }

        var runs = ScriptItemizer.Itemize(start == 0 && end == text.Length ? text : text.Substring(start, end - start));
        if (runs.Count == 1)
        {
            return ShapeRun(font, text, start, end, start, runs[0].Script, options);
        }

        return runs.SelectMany(run =>
            ShapeRun(font, text, start + run.Start, start + run.End, start, run.Script, options)).ToArray();
    }

    private static ShapedGlyph[] ShapeRun(IFont font, string text, int start, int end, int origin, string script,
        ShapingOptions options)
    {
        var buffer = _spareBuffer ?? new GlyphBuffer(end - start);
        _spareBuffer = null;
        buffer.Reset(end - start);
        try
        {
            return ShapeRun(font, text, start, end, origin, script, options, buffer);
        }
        finally
        {
            _spareBuffer = buffer.Info.Length <= GlyphBuffer.MostKept ? buffer : null;
        }
    }

    private static ShapedGlyph[] ShapeRun(IFont font, string text, int start, int end, int origin, string script,
        ShapingOptions options, GlyphBuffer buffer)
    {
        AddText(buffer, text, start, end);
        UnicodeProps.SetAll(buffer);
        FormClusters(buffer);

        var rightToLeft = !options.Vertical && (options.Direction == TextDirection.RightToLeft ||
                                                (options.Direction == TextDirection.Auto && IsRightToLeft(script)));
        var layout = font.Layout;
        var plan = GetPlan(layout, script, options, font.NormalizedCoordinates, rightToLeft);

        if (rightToLeft)
        {
            Mirror(font, buffer, plan);
        }

        if (options.Vertical && plan.GetFeature("vert") == null)
        {
            ToVerticalForms(font, buffer);
        }

        // Hebrew in a font that positions no marks: a letter and its point are set as one of the presentation forms the
        // font has, as HarfBuzz sets them.
        Normalizer.Normalize(font, buffer, plan.JoinsLetters,
            script == "Hebr" && plan.GetFeature("mark") is not { GposIndex: >= 0 });
        SetupMasks(buffer, plan, options, text, start, end, origin);
        if (plan.FallbackMarkPositioning)
        {
            Positioner.RecategorizeMarks(buffer);
        }

        SetGlyphProps(buffer, layout);

        var applier = new LookupApplier(layout, buffer, font.NormalizedCoordinates)
        {
            RightToLeft = rightToLeft,
            Vertical = options.Vertical,
        };
        // A Syriac abbreviation mark the font's 'stch' cut into pieces: what that stage multiplied is marked right after it.
        var stretchStage = plan.JoinsLetters && plan.GetFeature("stch") is { GsubIndex: >= 0 } stch ? stch.GsubStage : -1;
        // Arabic in a font with no joining forms: the presentation forms it maps are set right after 'rlig'.
        var fallbackStage = ArabicFallback.Needed(plan) ? plan.GetFeature("rlig").GsubStage : -1;
        var stretches = false;
        applier.ApplyGsub(plan, stretchStage < 0 && fallbackStage < 0 ? null : stage =>
        {
            if (stage == stretchStage)
            {
                stretches = MarkStretches(buffer);
            }

            if (stage == fallbackStage)
            {
                ArabicFallback.Apply(font, plan, applier);
            }
        });
        // A ligature's components run the way the text does, or, down vertical lines, as its script reads; HarfBuzz
        // gives Old Italic, Runic and Tifinagh no direction and takes them as right to left.
        Positioner.Position(font, buffer, plan, applier, rightToLeft,
            options.Vertical ? IsRightToLeft(script) || script is "Ital" or "Runr" or "Tfng" : rightToLeft);
        HideDefaultIgnorables(font, buffer);

        var result = new ShapedGlyph[buffer.Length];
        for (var i = 0; i < buffer.Length; i++)
        {
            var info = buffer.Info[i];
            var placement = buffer.Placements[i];
            result[rightToLeft ? buffer.Length - 1 - i : i] = new ShapedGlyph(info.Glyph, info.Cluster - origin,
                placement.XAdvance, placement.YAdvance, placement.XOffset, placement.YOffset);
        }

        return stretches ? Stretched(font, buffer, result, rightToLeft) : result;
    }

    // The pieces 'stch' multiplied a mark into: the odd ones repeat, the even ones stand once (HarfBuzz's record_stch).
    private static bool MarkStretches(GlyphBuffer buffer)
    {
        var any = false;
        for (var i = 0; i < buffer.Length; i++)
        {
            ref var info = ref buffer.Info[i];
            if (info.IsMultiplied)
            {
                info.Stretch = (byte)(info.LigComp % 2 == 1 ? 2 : 1);
                any = true;
            }
        }

        return any;
    }

    // Each stretched mark spread over the rest of its word, as HarfBuzz's apply_stch: its repeating pieces copied as
    // many times as the word needs, a little closer together to fill it exactly, all of them with no advance of their
    // own. The glyphs come in drawing order.
    private static ShapedGlyph[] Stretched(IFont font, GlyphBuffer buffer, ShapedGlyph[] glyphs, bool rightToLeft)
    {
        var count = glyphs.Length;
        var actions = new byte[count];
        var words = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var info = buffer.Info[rightToLeft ? count - 1 - i : i];
            actions[i] = info.Stretch;
            words[i] = info.IsDefaultIgnorable || IsWordCategory(info.Category);
        }

        var built = new List<ShapedGlyph>(count + 16);
        for (var i = count; i > 0; i--)
        {
            if (actions[i - 1] == 0)
            {
                built.Add(glyphs[i - 1]);
                continue;
            }

            int total = 0, fixedWidth = 0, repeatingWidth = 0, repeating = 0;
            var end = i;
            while (i > 0 && actions[i - 1] != 0)
            {
                i--;
                var width = font.GetAdvanceWidth(glyphs[i].GlyphIndex);
                if (actions[i] == 1)
                {
                    fixedWidth += width;
                }
                else
                {
                    repeatingWidth += width;
                    repeating++;
                }
            }

            var start = i;
            var context = i;
            while (context > 0 && actions[context - 1] == 0 && words[context - 1])
            {
                context--;
                total += glyphs[context].XAdvance;
            }

            var copies = 0;
            var remaining = total - fixedWidth;
            if (remaining > repeatingWidth && repeatingWidth > 0)
            {
                copies = remaining / repeatingWidth - 1;
            }

            var overlap = 0;
            var shortfall = remaining - repeatingWidth * (copies + 1);
            if (shortfall > 0 && repeating > 0)
            {
                copies++;
                var excess = (copies + 1) * repeatingWidth - remaining;
                if (excess > 0)
                {
                    overlap = excess / (copies * repeating);
                    remaining = 0;
                }
            }

            var offset = remaining / 2;
            for (var k = end; k > start; k--)
            {
                var piece = glyphs[k - 1];
                var width = font.GetAdvanceWidth(piece.GlyphIndex);
                var repeat = actions[k - 1] == 2 ? copies + 1 : 1;
                for (var n = 0; n < repeat; n++)
                {
                    if (rightToLeft)
                    {
                        offset -= width;
                        offset += n > 0 ? overlap : 0;
                    }

                    built.Add(new ShapedGlyph(piece.GlyphIndex, piece.Cluster, 0, piece.YAdvance, offset, piece.YOffset));
                    if (!rightToLeft)
                    {
                        offset += width;
                        offset -= n > 0 ? overlap : 0;
                    }
                }
            }

            i++;
        }

        built.Reverse();
        return built.ToArray();
    }

    // What a stretched mark spreads over: letters, marks, numbers and symbols, as HarfBuzz's word categories.
    private static bool IsWordCategory(UnicodeCategory category) => category is UnicodeCategory.OtherNotAssigned
        or UnicodeCategory.PrivateUse or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.NonSpacingMark
        or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
        or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.MathSymbol
        or UnicodeCategory.OtherSymbol;

    private static bool IsRightToLeft(string script) => script is "Arab" or "Hebr" or "Syrc" or "Thaa" or "Cprt" or "Khar"
        or "Phnx" or "Nkoo" or "Lydi" or "Avst" or "Armi" or "Phli" or "Prti" or "Sarb" or "Orkh" or "Samr" or "Mand"
        or "Merc" or "Mero" or "Narb" or "Nbat" or "Palm" or "Mani" or "Hatr" or "Hung" or "Phlp" or "Adlm" or "Rohg"
        or "Sogo" or "Sogd" or "Elym" or "Chrs" or "Yezi" or "Ougr" or "Mend" or "Gara";

    private static void Mirror(IFont font, GlyphBuffer buffer, ShapePlan plan)
    {
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            var mirror = BidiProperties.Mirror(info[i].Codepoint);
            if (mirror < 0 || !font.TryGetGlyphIndex(mirror, out _))
            {
                info[i].Mask |= plan.RtlmMask;
                continue;
            }

            info[i].Codepoint = mirror;
        }
    }

    private static void ToVerticalForms(IFont font, GlyphBuffer buffer)
    {
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            var form = VerticalForms.Of(info[i].Codepoint);
            if (form != info[i].Codepoint && font.TryGetGlyphIndex(form, out _))
            {
                info[i].Codepoint = form;
            }
        }
    }

    private static void AddText(GlyphBuffer buffer, string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            int codepoint = text[i];
            var start = i;
            if (char.IsHighSurrogate(text[i]) && i + 1 < to && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                codepoint = 0xFFFD;
            }

            buffer.Add(new GlyphInfo { Codepoint = codepoint, Cluster = start });
        }
    }

    private static void FormClusters(GlyphBuffer buffer)
    {
        if (!buffer.HasNonAscii)
        {
            return;
        }

        var start = 0;
        while (start < buffer.Length)
        {
            var end = start + 1;
            while (end < buffer.Length && buffer.Info[end].IsContinuation)
            {
                end++;
            }

            buffer.MergeClusters(start, end);
            start = end;
        }
    }


    private static ShapePlan GetPlan(OpenTypeLayout layout, string script, ShapingOptions options, float[] coordinates,
        bool rightToLeft)
    {
        var languageTag = OpenTypeTags.LanguageTag(options.Language);
        var features = string.Join(",", options.Features.Select(f => $"{f.Tag}={f.Value}{(f.IsGlobal ? "g" : "r")}"));
        var gsubVariation = layout.Gsub?.FeatureVariations?.Find(coordinates) ?? -1;
        var gposVariation = layout.Gpos?.FeatureVariations?.Find(coordinates) ?? -1;
        var key = $"{script}|{languageTag}|{features}|{gsubVariation}|{gposVariation}|" +
                  (options.Vertical ? "ttb" : rightToLeft ? "rtl" : "ltr");
        return layout.Plans.GetOrAdd(key,
            _ => ShapePlan.Create(layout, OpenTypeTags.ScriptTags(script), languageTag, options.Features, gsubVariation,
                gposVariation, rightToLeft, script, options.Vertical));
    }

    private static void SetupMasks(GlyphBuffer buffer, ShapePlan plan, ShapingOptions options, string text, int start,
        int end, int origin)
    {
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].Mask = plan.GlobalMask | (info[i].Mask & plan.RtlmMask);
        }

        if (plan.JoinsLetters)
        {
            ArabicShaper.SetupMasks(buffer, plan, text, start, end);
        }

        foreach (var feature in options.Features)
        {
            if (feature.IsGlobal)
            {
                continue;
            }

            var map = plan.GetFeature(feature.Tag);
            if (map == null)
            {
                continue;
            }

            var value = (feature.Value << map.Shift) & map.Mask;
            for (var i = 0; i < buffer.Length; i++)
            {
                var cluster = info[i].Cluster - origin;
                if (cluster >= feature.Start && cluster < feature.End)
                {
                    info[i].Mask = (info[i].Mask & ~map.Mask) | value;
                }
            }
        }

        if (buffer.HasNonAscii && plan.FracMask != 0 && plan.NumrMask != 0 && plan.DnomMask != 0)
        {
            SetupFractionMasks(buffer, plan);
        }
    }

    private static void SetupFractionMasks(GlyphBuffer buffer, ShapePlan plan)
    {
        var info = buffer.Info;
        var before = plan.NumrMask | plan.FracMask;
        var after = plan.FracMask | plan.DnomMask;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (info[i].Codepoint != 0x2044)
            {
                continue;
            }

            var start = i;
            var end = i + 1;
            while (start > 0 && info[start - 1].Category == UnicodeCategory.DecimalDigitNumber)
            {
                start--;
            }

            while (end < buffer.Length && info[end].Category == UnicodeCategory.DecimalDigitNumber)
            {
                end++;
            }

            if (start == i || end == i + 1)
            {
                continue;
            }

            for (var j = start; j < i; j++)
            {
                info[j].Mask |= before;
            }

            info[i].Mask |= plan.FracMask;
            for (var j = i + 1; j < end; j++)
            {
                info[j].Mask |= after;
            }

            i = end - 1;
        }
    }

    private static void SetGlyphProps(GlyphBuffer buffer, OpenTypeLayout layout)
    {
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].LigProps = 0;
            if (layout.HasGlyphClasses)
            {
                info[i].Props = layout.GetGlyphProps(info[i].Glyph);
                continue;
            }

            var isMark = info[i].Category == UnicodeCategory.NonSpacingMark && !info[i].IsDefaultIgnorable;
            info[i].Props = isMark ? GlyphProps.Mark : GlyphProps.BaseGlyph;
        }
    }

    private static void HideDefaultIgnorables(IFont font, GlyphBuffer buffer)
    {
        if (!buffer.HasDefaultIgnorables)
        {
            return;
        }

        if (font.TryGetGlyphIndex(0x0020, out var space))
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer.Info[i].IsDefaultIgnorable)
                {
                    buffer.Info[i].Glyph = space;
                }
            }

            return;
        }

        buffer.RemoveWhere(info => info.IsDefaultIgnorable);
    }
}
