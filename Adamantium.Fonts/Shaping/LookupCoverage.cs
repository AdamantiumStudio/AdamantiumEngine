using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Adamantium.Fonts.Tables.GPOS;
using Adamantium.Fonts.Tables.GSUB;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Shaping;

internal static class LookupCoverage
{
    private static readonly ConditionalWeakTable<ILookupTable, ulong[]> Built = new();

    public static ulong[] Of(ILookupTable table) => Built.GetValue(table, Build);

    public static bool Covers(ulong[] bits, uint glyph)
    {
        if (bits == null)
        {
            return true;
        }

        var word = (ushort)glyph >> 6;
        return word < bits.Length && (bits[word] & (1UL << (ushort)glyph)) != 0;
    }

    private static ulong[] Build(ILookupTable table)
    {
        var coverages = new List<CoverageTable>();
        foreach (var subtable in table.SubTables)
        {
            CoverageTable coverage;
            switch (subtable)
            {
                case SingleSubstitutionSubTableFormat1 single:
                    coverage = single.Coverage;
                    break;
                case SingleSubstitutionSubTableFormat2 single:
                    coverage = single.Coverage;
                    break;
                case MultipleSubstitutionSubTable multiple:
                    coverage = multiple.Coverage;
                    break;
                case AlternateSubstitutionSubTable alternate:
                    coverage = alternate.Coverage;
                    break;
                case LigatureSubstitutionSubTable ligature:
                    coverage = ligature.Coverage;
                    break;
                case ContextualSubstitutionSubTableFormat1 context:
                    coverage = context.Coverage;
                    break;
                case ContextualSubstitutionSubTableFormat2 context:
                    coverage = context.Coverage;
                    break;
                case ContextualSubstitutionSubTableFormat3 context:
                    coverage = First(context.CoverageTables);
                    break;
                case ChainedContextsSubstitutionSubTableFormat1 chain:
                    coverage = chain.Coverage;
                    break;
                case ChainedContextsSubstitutionSubTableFormat2 chain:
                    coverage = chain.Coverage;
                    break;
                case ChainedContextsSubstitutionSubTableFormat3 chain:
                    coverage = First(chain.InputCoverages);
                    break;
                case ReverseChainingContextualSubstitutionFormat1 reverse:
                    coverage = reverse.Coverage;
                    break;
                case SingleAdjustmentPositioningSubTable single:
                    coverage = single.Coverage;
                    break;
                case PairAdjustmentPositioningSubTableFormat1 pair:
                    coverage = pair.CoverageTable;
                    break;
                case PairAdjustmentPositioningSubTableFormat2 pair:
                    coverage = pair.CoverageTable;
                    break;
                case CursiveAttachmentPositioningSubTable cursive:
                    coverage = cursive.Coverage;
                    break;
                case MarkToBaseAttachmentPositioningSubTable markToBase:
                    coverage = markToBase.MarkCoverage;
                    break;
                case MarkToLigatureAttachmentPositioningSubTable markToLigature:
                    coverage = markToLigature.MarkCoverage;
                    break;
                case MarkToMarkAttachmentPositioningSubtable markToMark:
                    coverage = markToMark.Mark1Coverage;
                    break;
                case ContextualPositioningSubtableFormat1 context:
                    coverage = context.Coverage;
                    break;
                case ContextualPositioningSubtableFormat2 context:
                    coverage = context.Coverage;
                    break;
                case ContextualPositioningSubtableFormat3 context:
                    coverage = First(context.CoverageTables);
                    break;
                case ChainedContextsPositioningSubtableFormat1 chain:
                    coverage = chain.Coverage;
                    break;
                case ChainedContextsPositioningSubtableFormat2 chain:
                    coverage = chain.Coverage;
                    break;
                case ChainedContextsPositioningSubtableFormat3 chain:
                    coverage = First(chain.InputCoverages);
                    break;
                default:
                    return null;
            }

            switch (coverage)
            {
                case null:
                    continue;
                case CoverageTableFormat1 or CoverageTableFormat2:
                    coverages.Add(coverage);
                    break;
                default:
                    return null;
            }
        }

        var last = -1;
        foreach (var coverage in coverages)
        {
            last = Math.Max(last, LastGlyph(coverage));
        }

        var bits = new ulong[(last >> 6) + 1];
        foreach (var coverage in coverages)
        {
            if (coverage is CoverageTableFormat1 list)
            {
                foreach (var glyph in list.GlyphIdArray)
                {
                    bits[glyph >> 6] |= 1UL << glyph;
                }

                continue;
            }

            var ranges = (CoverageTableFormat2)coverage;
            for (var i = 0; i < ranges.RangeCount; i++)
            {
                for (int glyph = ranges.StartIndices[i]; glyph <= ranges.EndIndices[i]; glyph++)
                {
                    bits[glyph >> 6] |= 1UL << glyph;
                }
            }
        }

        return bits;
    }

    private static int LastGlyph(CoverageTable coverage)
    {
        var last = -1;
        if (coverage is CoverageTableFormat1 list)
        {
            foreach (var glyph in list.GlyphIdArray)
            {
                last = Math.Max(last, glyph);
            }

            return last;
        }

        var ranges = (CoverageTableFormat2)coverage;
        for (var i = 0; i < ranges.RangeCount; i++)
        {
            last = Math.Max(last, ranges.EndIndices[i]);
        }

        return last;
    }

    private static CoverageTable First(CoverageTable[] coverages) => coverages is { Length: > 0 } ? coverages[0] : null;
}
