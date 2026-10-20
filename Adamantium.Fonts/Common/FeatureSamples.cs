using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.Fonts.Shaping;

namespace Adamantium.Fonts.Common;

/// <summary>What shows a font's substitution features - a stylistic set, a character variant, a swash: the characters
/// each turns into other glyphs, to draw with the feature on in a gallery or a list of the font's features.</summary>
public static class FeatureSamples
{
    /// <summary>The sample of each of <paramref name="features"/> in <paramref name="font"/>, by tag: the font's own
    /// sample text where it gives one, otherwise up to <paramref name="count"/> characters the feature changes as typed -
    /// the ones a character variant names first, then Latin letters and digits, then the rest by code point. The letters
    /// are of one script, one form of a letter and no compatibility form, mark or space; signs of no script show a
    /// feature that changes no letter. A feature that changes no character on its own is left out.</summary>
    public static IReadOnlyDictionary<string, string> Of(IFont font, IEnumerable<Feature> features, int count = 4)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "A sample has at least one character.");
        }

        var samples = new Dictionary<string, string>();
        var found = new Dictionary<string, Sample>();
        foreach (var feature in features)
        {
            var tag = feature.Info.Tag;
            if (!string.IsNullOrWhiteSpace(feature.SampleText))
            {
                samples[tag] = feature.SampleText;
                found.Remove(tag);
                continue;
            }

            if (samples.ContainsKey(tag))
            {
                continue;
            }

            if (!found.TryGetValue(tag, out var sample))
            {
                found[tag] = sample = new Sample(count);
            }

            foreach (var codepoint in feature.Characters)
            {
                if (Shows(codepoint) && font.TryGetGlyphIndex(codepoint, out var glyph) &&
                    font.GetGlyphAlternates(glyph).Any(alternate => alternate.Feature == tag && alternate.Value == 1))
                {
                    sample.Take(glyph, codepoint);
                }
            }
        }

        var left = found.Values.Count(sample => !sample.Full);
        foreach (var codepoint in Order(font))
        {
            if (left == 0)
            {
                break;
            }

            if (!font.TryGetGlyphIndex(codepoint, out var glyph))
            {
                continue;
            }

            foreach (var alternate in font.GetGlyphAlternates(glyph))
            {
                if (alternate.Value != 1 || !found.TryGetValue(alternate.Feature, out var sample) || sample.Full)
                {
                    continue;
                }

                sample.Take(glyph, codepoint);
                left -= sample.Full ? 1 : 0;
            }
        }

        foreach (var pair in found)
        {
            if (pair.Value.Text is { } text)
            {
                samples[pair.Key] = text;
            }
        }

        return samples;
    }

    private static IEnumerable<int> Order(IFont font)
    {
        const string first = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        foreach (var c in first)
        {
            yield return c;
        }

        var composed = new List<int>();
        foreach (var unicode in font.Unicodes.OrderBy(unicode => unicode))
        {
            var codepoint = (int)unicode;
            if ((codepoint > 0x7F || !char.IsLetterOrDigit((char)codepoint)) && Shows(codepoint))
            {
                if (Base(codepoint) == codepoint)
                {
                    yield return codepoint;
                }
                else
                {
                    composed.Add(codepoint);
                }
            }
        }

        foreach (var codepoint in composed)
        {
            yield return codepoint;
        }
    }

    private static int Base(int codepoint)
    {
        while (codepoint is not (>= 0xAC00 and <= 0xD7A3) && UnicodeData.TryDecompose(codepoint, out var first, out _))
        {
            codepoint = first;
        }

        return codepoint;
    }

    private static bool Shows(int codepoint)
    {
        if (codepoint is < 0x21 or > 0x10FFFF or >= 0xD800 and <= 0xDFFF)
        {
            return false;
        }

        return UnicodeData.GetCategory(codepoint) is not (UnicodeCategory.Control or UnicodeCategory.Format or
                   UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or
                   UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or
                   UnicodeCategory.OtherNotAssigned or UnicodeCategory.NonSpacingMark or
                   UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) &&
               char.ConvertFromUtf32(codepoint).IsNormalized(NormalizationForm.FormKC);
    }

    private sealed class Sample(int count)
    {
        private readonly List<int> _letters = [];
        private readonly List<int> _signs = [];
        private readonly HashSet<uint> _glyphs = [];
        private readonly HashSet<int> _bases = [];
        private string _script;

        public bool Full => _letters.Count == count;

        public string Text
        {
            get
            {
                var characters = _letters.Count > 0 ? _letters : _signs;
                return characters.Count > 0 ? string.Concat(characters.Select(char.ConvertFromUtf32)) : null;
            }
        }

        public void Take(uint glyph, int codepoint)
        {
            if (Full || _glyphs.Contains(glyph))
            {
                return;
            }

            var script = UnicodeData.GetScript(codepoint);
            var ofNoScript = script is "Zyyy" or "Zinh" or "Zzzz";
            if (ofNoScript && codepoint is not (>= '0' and <= '9'))
            {
                if (_signs.Count < count)
                {
                    _glyphs.Add(glyph);
                    _signs.Add(codepoint);
                }

                return;
            }

            if (!ofNoScript && _script != null && script != _script || !_bases.Add(Base(codepoint)))
            {
                return;
            }

            _script ??= ofNoScript ? null : script;
            _glyphs.Add(glyph);
            _letters.Add(codepoint);
        }
    }
}
