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

        Normalizer.Normalize(font, buffer, plan.JoinsLetters);
        SetupMasks(buffer, plan, options, text, start, end, origin);
        SetGlyphProps(buffer, layout);

        var applier = new LookupApplier(layout, buffer, font.NormalizedCoordinates)
        {
            RightToLeft = rightToLeft,
            Vertical = options.Vertical,
        };
        applier.ApplyGsub(plan);
        Positioner.Position(font, buffer, plan, applier, rightToLeft);
        HideDefaultIgnorables(font, buffer);

        var result = new ShapedGlyph[buffer.Length];
        for (var i = 0; i < buffer.Length; i++)
        {
            var info = buffer.Info[i];
            var placement = buffer.Placements[i];
            result[rightToLeft ? buffer.Length - 1 - i : i] = new ShapedGlyph(info.Glyph, info.Cluster - origin,
                placement.XAdvance, placement.YAdvance, placement.XOffset, placement.YOffset);
        }

        return result;
    }

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
        var scriptTag = OpenTypeTags.ScriptTag(script);
        var languageTag = OpenTypeTags.LanguageTag(options.Language);
        var features = string.Join(",", options.Features.Select(f => $"{f.Tag}={f.Value}{(f.IsGlobal ? "g" : "r")}"));
        var gsubVariation = layout.Gsub?.FeatureVariations?.Find(coordinates) ?? -1;
        var gposVariation = layout.Gpos?.FeatureVariations?.Find(coordinates) ?? -1;
        var key = $"{script}|{scriptTag}|{languageTag}|{features}|{gsubVariation}|{gposVariation}|" +
                  (options.Vertical ? "ttb" : rightToLeft ? "rtl" : "ltr");
        return layout.Plans.GetOrAdd(key,
            _ => ShapePlan.Create(layout, scriptTag, languageTag, options.Features, gsubVariation, gposVariation,
                rightToLeft, script, options.Vertical));
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
