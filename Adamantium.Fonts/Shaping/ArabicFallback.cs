using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.Fonts.Tables.GSUB;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Shaping;

// Arabic in a font whose GSUB has no joining forms, as HarfBuzz's fallback: lookups made from the presentation forms of
// Unicode the font maps (U+FB50-FDFF, U+FE70-FEFF) set the joining forms, then the ligatures of HarfBuzz 8.3's tables.
internal static class ArabicFallback
{
    // A letter, then its initial, medial, final and isolated presentation forms, 0 where Unicode has none: from the
    // compatibility decompositions of UnicodeData, the last form of a kind winning, as HarfBuzz's shaping table.
    private static readonly int[][] Forms =
    [
        [0x0621, 0, 0, 0, 0xFE80], [0x0622, 0, 0, 0xFE82, 0xFE81], [0x0623, 0, 0, 0xFE84, 0xFE83],
        [0x0624, 0, 0, 0xFE86, 0xFE85], [0x0625, 0, 0, 0xFE88, 0xFE87], [0x0626, 0xFE8B, 0xFE8C, 0xFE8A, 0xFE89],
        [0x0627, 0, 0, 0xFE8E, 0xFE8D], [0x0628, 0xFE91, 0xFE92, 0xFE90, 0xFE8F], [0x0629, 0, 0, 0xFE94, 0xFE93],
        [0x062A, 0xFE97, 0xFE98, 0xFE96, 0xFE95], [0x062B, 0xFE9B, 0xFE9C, 0xFE9A, 0xFE99],
        [0x062C, 0xFE9F, 0xFEA0, 0xFE9E, 0xFE9D], [0x062D, 0xFEA3, 0xFEA4, 0xFEA2, 0xFEA1],
        [0x062E, 0xFEA7, 0xFEA8, 0xFEA6, 0xFEA5], [0x062F, 0, 0, 0xFEAA, 0xFEA9], [0x0630, 0, 0, 0xFEAC, 0xFEAB],
        [0x0631, 0, 0, 0xFEAE, 0xFEAD], [0x0632, 0, 0, 0xFEB0, 0xFEAF], [0x0633, 0xFEB3, 0xFEB4, 0xFEB2, 0xFEB1],
        [0x0634, 0xFEB7, 0xFEB8, 0xFEB6, 0xFEB5], [0x0635, 0xFEBB, 0xFEBC, 0xFEBA, 0xFEB9],
        [0x0636, 0xFEBF, 0xFEC0, 0xFEBE, 0xFEBD], [0x0637, 0xFEC3, 0xFEC4, 0xFEC2, 0xFEC1],
        [0x0638, 0xFEC7, 0xFEC8, 0xFEC6, 0xFEC5], [0x0639, 0xFECB, 0xFECC, 0xFECA, 0xFEC9],
        [0x063A, 0xFECF, 0xFED0, 0xFECE, 0xFECD], [0x0641, 0xFED3, 0xFED4, 0xFED2, 0xFED1],
        [0x0642, 0xFED7, 0xFED8, 0xFED6, 0xFED5], [0x0643, 0xFEDB, 0xFEDC, 0xFEDA, 0xFED9],
        [0x0644, 0xFEDF, 0xFEE0, 0xFEDE, 0xFEDD], [0x0645, 0xFEE3, 0xFEE4, 0xFEE2, 0xFEE1],
        [0x0646, 0xFEE7, 0xFEE8, 0xFEE6, 0xFEE5], [0x0647, 0xFEEB, 0xFEEC, 0xFEEA, 0xFEE9],
        [0x0648, 0, 0, 0xFEEE, 0xFEED], [0x0649, 0xFBE8, 0xFBE9, 0xFEF0, 0xFEEF],
        [0x064A, 0xFEF3, 0xFEF4, 0xFEF2, 0xFEF1], [0x0671, 0, 0, 0xFB51, 0xFB50], [0x0677, 0, 0, 0, 0xFBDD],
        [0x0679, 0xFB68, 0xFB69, 0xFB67, 0xFB66], [0x067A, 0xFB60, 0xFB61, 0xFB5F, 0xFB5E],
        [0x067B, 0xFB54, 0xFB55, 0xFB53, 0xFB52], [0x067E, 0xFB58, 0xFB59, 0xFB57, 0xFB56],
        [0x067F, 0xFB64, 0xFB65, 0xFB63, 0xFB62], [0x0680, 0xFB5C, 0xFB5D, 0xFB5B, 0xFB5A],
        [0x0683, 0xFB78, 0xFB79, 0xFB77, 0xFB76], [0x0684, 0xFB74, 0xFB75, 0xFB73, 0xFB72],
        [0x0686, 0xFB7C, 0xFB7D, 0xFB7B, 0xFB7A], [0x0687, 0xFB80, 0xFB81, 0xFB7F, 0xFB7E],
        [0x0688, 0, 0, 0xFB89, 0xFB88], [0x068C, 0, 0, 0xFB85, 0xFB84], [0x068D, 0, 0, 0xFB83, 0xFB82],
        [0x068E, 0, 0, 0xFB87, 0xFB86], [0x0691, 0, 0, 0xFB8D, 0xFB8C], [0x0698, 0, 0, 0xFB8B, 0xFB8A],
        [0x06A4, 0xFB6C, 0xFB6D, 0xFB6B, 0xFB6A], [0x06A6, 0xFB70, 0xFB71, 0xFB6F, 0xFB6E],
        [0x06A9, 0xFB90, 0xFB91, 0xFB8F, 0xFB8E], [0x06AD, 0xFBD5, 0xFBD6, 0xFBD4, 0xFBD3],
        [0x06AF, 0xFB94, 0xFB95, 0xFB93, 0xFB92], [0x06B1, 0xFB9C, 0xFB9D, 0xFB9B, 0xFB9A],
        [0x06B3, 0xFB98, 0xFB99, 0xFB97, 0xFB96], [0x06BA, 0, 0, 0xFB9F, 0xFB9E],
        [0x06BB, 0xFBA2, 0xFBA3, 0xFBA1, 0xFBA0], [0x06BE, 0xFBAC, 0xFBAD, 0xFBAB, 0xFBAA],
        [0x06C0, 0, 0, 0xFBA5, 0xFBA4], [0x06C1, 0xFBA8, 0xFBA9, 0xFBA7, 0xFBA6], [0x06C5, 0, 0, 0xFBE1, 0xFBE0],
        [0x06C6, 0, 0, 0xFBDA, 0xFBD9], [0x06C7, 0, 0, 0xFBD8, 0xFBD7], [0x06C8, 0, 0, 0xFBDC, 0xFBDB],
        [0x06C9, 0, 0, 0xFBE3, 0xFBE2], [0x06CB, 0, 0, 0xFBDF, 0xFBDE], [0x06CC, 0xFBFE, 0xFBFF, 0xFBFD, 0xFBFC],
        [0x06D0, 0xFBE6, 0xFBE7, 0xFBE5, 0xFBE4], [0x06D2, 0, 0, 0xFBAF, 0xFBAE], [0x06D3, 0, 0, 0xFBB1, 0xFBB0],
    ];

    private static readonly string[] Features = ["init", "medi", "fina", "isol", "rlig", "rlig", "rlig"];

    // The ligatures of three letters' forms, as HarfBuzz's table (two of them in its private use forms).
    private static readonly (int First, int[] Components, int Ligature)[] ThreeLetters =
    [
        (0xFEDF, [0xFEE4, 0xFEA4], 0xFD88), (0xFEDF, [0xFEE0, 0xFEEA], 0xF201), (0xFEDF, [0xFEE4, 0xFEA0], 0xF211),
    ];

    // The ligatures of two letters' forms, lam with alef among them, as HarfBuzz's table.
    private static readonly (int First, int[] Components, int Ligature)[] TwoLetters =
    [
        (0xFE91, [0xFEE2], 0xFC08), (0xFE91, [0xFEE4], 0xFC9F), (0xFE91, [0xFEA0], 0xFC9C), (0xFE91, [0xFEA4], 0xFC9D),
        (0xFE91, [0xFEA8], 0xFC9E), (0xFE92, [0xFEAE], 0xFC6A), (0xFE92, [0xFEE6], 0xFC6D), (0xFE92, [0xFEF2], 0xFC6F),
        (0xFE97, [0xFEE2], 0xFC0E), (0xFE97, [0xFEE4], 0xFCA4), (0xFE97, [0xFEA0], 0xFCA1), (0xFE97, [0xFEA4], 0xFCA2),
        (0xFE97, [0xFEA8], 0xFCA3), (0xFE98, [0xFEAE], 0xFC70), (0xFE98, [0xFEE6], 0xFC73), (0xFE98, [0xFEF2], 0xFC75),
        (0xFE9B, [0xFEE2], 0xFC12), (0xFE9F, [0xFEE4], 0xFCA8), (0xFEA3, [0xFEE4], 0xFCAA), (0xFEA7, [0xFEE4], 0xFCAC),
        (0xFEB3, [0xFEE4], 0xFCB0), (0xFEB7, [0xFEE4], 0xFD30), (0xFED3, [0xFEF2], 0xFC32), (0xFEDF, [0xFE9E], 0xFC3F),
        (0xFEDF, [0xFEA0], 0xFCC9), (0xFEDF, [0xFEA2], 0xFC40), (0xFEDF, [0xFEA4], 0xFCCA), (0xFEDF, [0xFEA6], 0xFC41),
        (0xFEDF, [0xFEA8], 0xFCCB), (0xFEDF, [0xFEE2], 0xFC42), (0xFEDF, [0xFEE4], 0xFCCC), (0xFEDF, [0xFEF2], 0xFC44),
        (0xFEDF, [0xFEEC], 0xFCCD), (0xFEDF, [0xFE82], 0xFEF5), (0xFEDF, [0xFE84], 0xFEF7), (0xFEDF, [0xFE88], 0xFEF9),
        (0xFEDF, [0xFE8E], 0xFEFB), (0xFEE0, [0xFEF0], 0xFC86), (0xFEE0, [0xFE82], 0xFEF6), (0xFEE0, [0xFE84], 0xFEF8),
        (0xFEE0, [0xFE88], 0xFEFA), (0xFEE0, [0xFE8E], 0xFEFC), (0xFEE3, [0xFEA0], 0xFCCE), (0xFEE3, [0xFEA4], 0xFCCF),
        (0xFEE3, [0xFEA8], 0xFCD0), (0xFEE3, [0xFEE4], 0xFCD1), (0xFEE7, [0xFEE2], 0xFC4E), (0xFEE7, [0xFEE4], 0xFCD5),
        (0xFEE7, [0xFEA0], 0xFCD2), (0xFEE7, [0xFEA4], 0xFCD3), (0xFEE8, [0xFEF2], 0xFC8F), (0xFEF3, [0xFEA0], 0xFCDA),
        (0xFEF3, [0xFEA4], 0xFCDB), (0xFEF3, [0xFEA8], 0xFCDC), (0xFEF3, [0xFEE4], 0xFCDD), (0xFEF4, [0xFEAE], 0xFC91),
        (0xFEF4, [0xFEE6], 0xFC94),
    ];

    // Shadda, then a vowel: their ligature, as HarfBuzz's mark ligature table.
    private static readonly (int First, int[] Components, int Ligature)[] ShaddaVowels =
    [
        (0x0651, [0x064C], 0xFC5E), (0x0651, [0x064E], 0xFC60), (0x0651, [0x064F], 0xFC61), (0x0651, [0x0650], 0xFC62),
        (0x0651, [0x064B], 0xF2EE),
    ];

    // A font's lookups in the order of Features, null where the font has none of the glyphs.
    private static readonly ConditionalWeakTable<IFont, GSUBLookupTable[]> Lookups = new();

    // As HarfBuzz: only when the font sets none of the joining forms Arabic has a fallback for.
    public static bool Needed(ShapePlan plan) =>
        plan.JoiningScript == "Arab"
        && plan.GetFeature("isol") is { GsubIndex: < 0, GposIndex: < 0 }
        && plan.GetFeature("fina") is { GsubIndex: < 0, GposIndex: < 0 }
        && plan.GetFeature("medi") is { GsubIndex: < 0, GposIndex: < 0 }
        && plan.GetFeature("init") is { GsubIndex: < 0, GposIndex: < 0 };

    public static void Apply(IFont font, ShapePlan plan, LookupApplier applier)
    {
        var lookups = Lookups.GetValue(font, Build);
        for (var i = 0; i < lookups.Length; i++)
        {
            var mask = plan.GetFeature(Features[i])?.Mask ?? 0;
            if (lookups[i] != null && mask != 0)
            {
                applier.ApplySynthesizedGsub(new PlannedLookup
                {
                    Index = -1,
                    Table = lookups[i],
                    Mask = mask,
                    AutoZwnj = true,
                    AutoZwj = true,
                    Coverage = LookupCoverage.Of(lookups[i]),
                });
            }
        }
    }

    private static GSUBLookupTable[] Build(IFont font)
    {
        var lookups = new GSUBLookupTable[Features.Length];
        for (var form = 0; form < 4; form++)
        {
            var substitutes = new SortedDictionary<ushort, ushort>();
            foreach (var row in Forms)
            {
                if (row[form + 1] != 0 && font.TryGetGlyphIndex(row[0], out var letter)
                    && font.TryGetGlyphIndex(row[form + 1], out var glyph) && glyph != letter
                    && !substitutes.ContainsKey((ushort)letter))
                {
                    substitutes[(ushort)letter] = (ushort)glyph;
                }
            }

            lookups[form] = substitutes.Count == 0 ? null : Lookup(LookupFlags.IgnoreMarks, new SingleSubstitutionSubTableFormat2
            {
                Coverage = new CoverageTableFormat1 { GlyphIdArray = substitutes.Keys.ToArray() },
                SubstituteGlyphIds = substitutes.Values.ToArray(),
            });
        }

        lookups[4] = Ligatures(font, ThreeLetters, LookupFlags.IgnoreMarks);
        lookups[5] = Ligatures(font, TwoLetters, LookupFlags.IgnoreMarks);
        lookups[6] = Ligatures(font, ShaddaVowels, 0);
        return lookups;
    }

    private static GSUBLookupTable Ligatures(IFont font, (int First, int[] Components, int Ligature)[] table,
        LookupFlags flags)
    {
        var sets = new SortedDictionary<ushort, List<LigatureTable>>();
        foreach (var (first, components, ligature) in table)
        {
            var glyphs = new ushort[components.Length];
            var mapped = font.TryGetGlyphIndex(first, out var a) & font.TryGetGlyphIndex(ligature, out var l);
            for (var i = 0; mapped && i < components.Length; i++)
            {
                mapped = font.TryGetGlyphIndex(components[i], out var glyph);
                glyphs[i] = (ushort)glyph;
            }

            if (!mapped)
            {
                continue;
            }

            if (!sets.TryGetValue((ushort)a, out var set))
            {
                sets[(ushort)a] = set = [];
            }

            set.Add(new LigatureTable { LigatureGlyphID = (ushort)l, ComponentGlypIDs = glyphs });
        }

        return sets.Count == 0 ? null : Lookup(flags, new LigatureSubstitutionSubTable
        {
            Coverage = new CoverageTableFormat1 { GlyphIdArray = sets.Keys.ToArray() },
            LigatureSetTables = sets.Values.Select(set => new LigatureSetTable { Ligatures = set.ToArray() }).ToArray(),
        });
    }

    private static GSUBLookupTable Lookup(LookupFlags flags, GSUBLookupSubTable subtable) => new()
    {
        LookupType = (ushort)subtable.Type,
        LookupFlag = (ushort)flags,
        SubTables = [subtable],
    };
}
