using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Shaping;

internal sealed partial class LookupApplier
{
    private bool ApplyGlyphContext(CoverageTable coverage, SequenceRuleSetTable[] ruleSets, ushort glyph)
    {
        var coverageIndex = coverage.FindPosition(glyph);
        if (coverageIndex < 0 || coverageIndex >= ruleSets.Length || ruleSets[coverageIndex]?.Rules == null)
        {
            return false;
        }

        foreach (var rule in ruleSets[coverageIndex].Rules)
        {
            var input = rule.InputSequence;
            if (ApplyContext(input.Length + 1, (i, info) => info.Glyph == input[i - 1], 0, null, 0, null,
                    rule.SeqLookupRecords))
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyClassContext(CoverageTable coverage, ClassDefTable classDef, ClassSequenceRuleSetTable[] ruleSets,
        ushort glyph)
    {
        if (coverage.FindPosition(glyph) < 0)
        {
            return false;
        }

        var glyphClass = classDef.GetClassValue(glyph);
        if (glyphClass >= ruleSets.Length || ruleSets[glyphClass]?.Rules == null)
        {
            return false;
        }

        return ApplyClassRules(classDef, ruleSets[glyphClass].Rules);
    }

    private bool ApplyClassRules(ClassDefTable classDef, ClassSequenceRuleTable[] rules)
    {
        foreach (var rule in rules)
        {
            var input = rule.InputSequence;
            if (ApplyContext(input.Length + 1, (i, info) => classDef.GetClassValue((ushort)info.Glyph) == input[i - 1],
                    0, null, 0, null, rule.LookupRecords))
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyCoverageContext(CoverageTable[] coverages, SequenceLookupRecord[] records)
    {
        if (coverages.Length == 0 || coverages[0].FindPosition((ushort)_buffer.Info[_index].Glyph) < 0)
        {
            return false;
        }

        return ApplyCoverageRule(coverages, records);
    }

    private bool ApplyCoverageRule(CoverageTable[] coverages, SequenceLookupRecord[] records)
    {
        return ApplyContext(coverages.Length, (i, info) => coverages[i].FindPosition((ushort)info.Glyph) >= 0, 0, null,
            0, null, records);
    }

    private bool ApplyGlyphChain(CoverageTable coverage, ChainedSequenceRuleSetTable[] ruleSets, ushort glyph)
    {
        var coverageIndex = coverage.FindPosition(glyph);
        if (coverageIndex < 0 || coverageIndex >= ruleSets.Length || ruleSets[coverageIndex]?.Rules == null)
        {
            return false;
        }

        foreach (var rule in ruleSets[coverageIndex].Rules)
        {
            var backtrack = rule.BacktrackSequence;
            var input = rule.InputSequence;
            var lookahead = rule.LookaheadSequence;
            if (ApplyContext(input.Length + 1, (i, info) => info.Glyph == input[i - 1],
                    backtrack.Length, (i, info) => info.Glyph == backtrack[i],
                    lookahead.Length, (i, info) => info.Glyph == lookahead[i], rule.SeqLookupRecords))
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyClassChain(CoverageTable coverage, ClassDefTable backtrackClasses, ClassDefTable inputClasses,
        ClassDefTable lookaheadClasses, ChainedSequenceRuleSetTable[] ruleSets, ushort glyph)
    {
        if (coverage.FindPosition(glyph) < 0)
        {
            return false;
        }

        var glyphClass = inputClasses.GetClassValue(glyph);
        if (glyphClass >= ruleSets.Length || ruleSets[glyphClass]?.Rules == null)
        {
            return false;
        }

        return ApplyClassChainRules(backtrackClasses, inputClasses, lookaheadClasses, ruleSets[glyphClass].Rules);
    }

    private bool ApplyClassChainRules(ClassDefTable backtrackClasses, ClassDefTable inputClasses,
        ClassDefTable lookaheadClasses, ChainedSequenceRuleTable[] rules)
    {
        foreach (var rule in rules)
        {
            var backtrack = rule.BacktrackSequence;
            var input = rule.InputSequence;
            var lookahead = rule.LookaheadSequence;
            if (ApplyContext(input.Length + 1, (i, info) => inputClasses.GetClassValue((ushort)info.Glyph) == input[i - 1],
                    backtrack.Length, (i, info) => backtrackClasses.GetClassValue((ushort)info.Glyph) == backtrack[i],
                    lookahead.Length, (i, info) => lookaheadClasses.GetClassValue((ushort)info.Glyph) == lookahead[i],
                    rule.SeqLookupRecords))
            {
                return true;
            }
        }

        return false;
    }

    private bool ApplyCoverageChain(CoverageTable[] backtrack, CoverageTable[] input, CoverageTable[] lookahead,
        SequenceLookupRecord[] records)
    {
        if (input.Length == 0 || input[0].FindPosition((ushort)_buffer.Info[_index].Glyph) < 0)
        {
            return false;
        }

        return ApplyCoverageChainRule(backtrack, input, lookahead, records);
    }

    private bool ApplyCoverageChainRule(CoverageTable[] backtrack, CoverageTable[] input, CoverageTable[] lookahead,
        SequenceLookupRecord[] records)
    {
        return ApplyContext(input.Length, (i, info) => input[i].FindPosition((ushort)info.Glyph) >= 0,
            backtrack.Length, (i, info) => backtrack[i].FindPosition((ushort)info.Glyph) >= 0,
            lookahead.Length, (i, info) => lookahead[i].FindPosition((ushort)info.Glyph) >= 0, records);
    }
}
