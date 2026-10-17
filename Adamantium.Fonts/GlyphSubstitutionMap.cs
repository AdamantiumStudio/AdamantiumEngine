using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Shaping;
using Adamantium.Fonts.Tables.GSUB;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts;

internal sealed class GlyphSubstitutionMap
{
    private const int MaxRounds = 16;

    private readonly Font font;
    private readonly Dictionary<uint, List<GlyphAlternate>> alternates = new();
    private readonly Dictionary<uint, List<string>> texts = new();
    private readonly ConcurrentDictionary<(string Script, string Language, string Feature, int Variation),
        Dictionary<uint, uint[]>> scoped = new();

    public GlyphSubstitutionMap(Font font)
    {
        this.font = font;
        var lookups = font.Layout.Gsub?.LookupList ?? [];
        CollectAlternates(font.FeatureCatalog, lookups);
        CollectTexts(font, lookups);
    }

    public IReadOnlyList<GlyphAlternate> Alternates(uint glyph) =>
        alternates.TryGetValue(glyph, out var list) ? list : [];

    public IReadOnlyList<uint> Alternates(uint glyph, string feature, int codepoint, string language, float[] coordinates)
    {
        var variation = font.Layout.Gsub?.FeatureVariations?.Find(coordinates) ?? -1;
        var map = scoped.GetOrAdd((UnicodeData.GetScript(codepoint), language, feature, variation), CollectScoped);
        return map.TryGetValue(glyph, out var outputs) ? outputs : [];
    }

    public IReadOnlyList<string> Text(uint glyph) => texts.TryGetValue(glyph, out var list) ? list : [];

    private void CollectAlternates(FeatureCatalog catalog, ILookupTable[] lookups)
    {
        var seen = new HashSet<(uint, string, int, uint)>();
        foreach (var feature in catalog.GSUBFeatures)
        {
            foreach (var index in feature.Lookups.Where(index => index < lookups.Length))
            {
                foreach (var (glyph, outputs, single) in Substitutions(lookups[index]))
                {
                    for (var i = 0; i < outputs.Length; i++)
                    {
                        var alternate = new GlyphAlternate(feature.Info.Tag, single ? 1 : i + 1, outputs[i]);
                        if (!seen.Add((glyph, alternate.Feature, alternate.Value, alternate.Glyph)))
                        {
                            continue;
                        }

                        if (!alternates.TryGetValue(glyph, out var list))
                        {
                            alternates[glyph] = list = [];
                        }

                        list.Add(alternate);
                    }
                }
            }
        }
    }

    private Dictionary<uint, uint[]> CollectScoped((string Script, string Language, string Feature, int Variation) key)
    {
        var map = new Dictionary<uint, uint[]>();
        var gsub = font.Layout.Gsub;
        var variation = key.Variation;
        var langSys = ShapePlan.Select(gsub, OpenTypeTags.ScriptTag(key.Script), OpenTypeTags.LanguageTag(key.Language),
            variation).LangSys;
        var index = ShapePlan.FindFeature(gsub, langSys, key.Feature);
        if (index < 0)
        {
            return map;
        }

        foreach (var lookup in ShapePlan.Feature(gsub, index, variation).LookupListIndices ?? [])
        {
            if (lookup >= gsub.LookupList.Length)
            {
                continue;
            }

            foreach (var (glyph, outputs, _) in Substitutions(gsub.LookupList[lookup]))
            {
                if (!map.ContainsKey(glyph))
                {
                    map[glyph] = outputs;
                }
            }
        }

        return map;
    }

    private void CollectTexts(Font font, ILookupTable[] lookups)
    {
        foreach (var pair in font.UnicodeToGlyph.OrderBy(pair => pair.Key))
        {
            if (pair.Key <= 0x10FFFF && pair.Key is < 0xD800 or > 0xDFFF)
            {
                AddText(pair.Value.Index, char.ConvertFromUtf32((int)pair.Key));
            }
        }

        var substitutions = lookups.SelectMany(Substitutions).ToArray();
        var ligatures = lookups.SelectMany(Ligatures).ToArray();
        var changed = true;
        for (var round = 0; changed && round < MaxRounds; round++)
        {
            changed = false;
            foreach (var (glyph, outputs, _) in substitutions)
            {
                if (!texts.TryGetValue(glyph, out var from))
                {
                    continue;
                }

                foreach (var output in outputs)
                {
                    foreach (var text in from.ToArray())
                    {
                        changed |= AddText(output, text);
                    }
                }
            }

            foreach (var (components, ligature) in ligatures)
            {
                if (components.All(texts.ContainsKey))
                {
                    var joined = new StringBuilder();
                    foreach (var component in components)
                    {
                        joined.Append(texts[component][0]);
                    }

                    changed |= AddText(ligature, joined.ToString());
                }
            }
        }
    }

    private bool AddText(uint glyph, string text)
    {
        if (!texts.TryGetValue(glyph, out var list))
        {
            texts[glyph] = list = [];
        }

        if (list.Contains(text))
        {
            return false;
        }

        list.Add(text);
        return true;
    }

    // A lookup's substitution for each glyph it covers, from the first subtable that covers it, as a lookup applies.
    private static IEnumerable<(uint Glyph, uint[] Outputs, bool Single)> Substitutions(ILookupTable lookup)
    {
        var covered = new HashSet<ushort>();
        foreach (var table in lookup.SubTables)
        {
            var coverage = table switch
            {
                SingleSubstitutionSubTableFormat1 single => single.Coverage,
                SingleSubstitutionSubTableFormat2 single => single.Coverage,
                AlternateSubstitutionSubTable alternate => alternate.Coverage,
                _ => null,
            };

            if (coverage == null)
            {
                continue;
            }

            foreach (var glyph in coverage.GetExpandedValues())
            {
                var position = coverage.FindPosition(glyph);
                if (position < 0 || !covered.Add(glyph))
                {
                    continue;
                }

                switch (table)
                {
                    case SingleSubstitutionSubTableFormat1 single:
                        yield return (glyph, [(ushort)(glyph + single.DeltaGlyphId)], true);
                        break;
                    case SingleSubstitutionSubTableFormat2 single when position < single.SubstituteGlyphIds.Length:
                        yield return (glyph, [single.SubstituteGlyphIds[position]], true);
                        break;
                    case AlternateSubstitutionSubTable alternate when position < alternate.AlternateSetTables.Length:
                        yield return (glyph,
                            alternate.AlternateSetTables[position].AlternateGlyphIDs.Select(id => (uint)id).ToArray(), false);
                        break;
                }
            }
        }
    }

    private static IEnumerable<(uint[] Components, uint Ligature)> Ligatures(ILookupTable lookup)
    {
        foreach (var table in lookup.SubTables.OfType<LigatureSubstitutionSubTable>())
        {
            foreach (var first in table.Coverage.GetExpandedValues())
            {
                var position = table.Coverage.FindPosition(first);
                if (position < 0 || position >= table.LigatureSetTables.Length)
                {
                    continue;
                }

                foreach (var ligature in table.LigatureSetTables[position].Ligatures)
                {
                    var components = new uint[ligature.ComponentGlypIDs.Length + 1];
                    components[0] = first;
                    for (var i = 0; i < ligature.ComponentGlypIDs.Length; i++)
                    {
                        components[i + 1] = ligature.ComponentGlypIDs[i];
                    }

                    yield return (components, ligature.LigatureGlyphID);
                }
            }
        }
    }
}
