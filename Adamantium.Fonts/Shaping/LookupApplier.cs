using System;
using Adamantium.Fonts.Tables.GSUB;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Shaping;

internal sealed partial class LookupApplier
{
    private const int MaxContextLength = 64;
    private const int MaxNestingLevel = 64;
    private const uint IgnoreFlags = (uint)(LookupFlags.IgnoreBaseGlyphs | LookupFlags.IgnoreLigatures | LookupFlags.IgnoreMarks);

    private readonly OpenTypeLayout _layout;
    private readonly GlyphBuffer _buffer;
    private readonly int[][] _positions = new int[MaxNestingLevel + 1][];
    private bool _gsub;
    private int _index;
    private uint _lookupMask;
    private uint _lookupProps;
    private bool _autoZwnj;
    private bool _autoZwj;
    private int _nestingLeft;
    private int _lastBase = -1;
    private int _lastBaseUntil;

    private readonly float[] _coordinates;

    public LookupApplier(OpenTypeLayout layout, GlyphBuffer buffer, float[] coordinates = null)
    {
        _layout = layout;
        _buffer = buffer;
        _coordinates = coordinates;
    }

    public bool RightToLeft { get; set; }

    public bool Vertical { get; set; }

    private enum SkipResult
    {
        No,
        Yes,
        Maybe,
    }

    private enum MatchResult
    {
        No,
        Yes,
        Maybe,
    }

    // afterStage hears the index of each stage once it is applied, as a pause between HarfBuzz's stages.
    public void ApplyGsub(ShapePlan plan, Action<int> afterStage = null)
    {
        _gsub = true;
        for (var stage = 0; stage < plan.GsubStages.Length; stage++)
        {
            foreach (var lookup in plan.GsubStages[stage])
            {
                ApplyLookup(lookup);
            }

            afterStage?.Invoke(stage);
        }
    }

    // A lookup the shaper made, not one of the font's, as HarfBuzz's fallback lookups.
    public void ApplySynthesizedGsub(in PlannedLookup lookup)
    {
        _gsub = true;
        ApplyLookup(lookup);
    }

    public void ApplyGpos(ShapePlan plan)
    {
        _gsub = false;
        foreach (var lookup in plan.GposLookups)
        {
            ApplyLookup(lookup);
        }
    }

    private void ApplyLookup(in PlannedLookup lookup)
    {
        _lookupMask = lookup.Mask;
        _autoZwnj = lookup.AutoZwnj;
        _autoZwj = lookup.AutoZwj;
        _lookupProps = PropsOf(lookup.Table);
        _nestingLeft = MaxNestingLevel;
        _lastBase = -1;
        _lastBaseUntil = 0;

        if (IsReverse(lookup.Table))
        {
            for (_index = _buffer.Length - 1; _index >= 0; _index--)
            {
                ref var info = ref _buffer.Info[_index];
                if ((info.Mask & _lookupMask) != 0 && LookupCoverage.Covers(lookup.Coverage, info.Glyph)
                    && CheckGlyphProperty(info, _lookupProps))
                {
                    ApplySubtables(lookup.Table);
                }
            }

            return;
        }

        _index = 0;
        while (_index < _buffer.Length)
        {
            ref var info = ref _buffer.Info[_index];
            if ((info.Mask & _lookupMask) != 0 && LookupCoverage.Covers(lookup.Coverage, info.Glyph)
                && CheckGlyphProperty(info, _lookupProps) && ApplySubtables(lookup.Table))
            {
                continue;
            }

            _index++;
        }
    }

    private bool ApplySubtables(ILookupTable table)
    {
        foreach (var subtable in table.SubTables)
        {
            if (_gsub ? ApplySubstitution(subtable) : ApplyPositioning(subtable))
            {
                return true;
            }
        }

        return false;
    }

    private bool Recurse(int lookupIndex)
    {
        IFontLayout table = _gsub ? _layout.Gsub : _layout.Gpos;
        if (_nestingLeft == 0 || lookupIndex >= table.LookupList.Length)
        {
            return false;
        }

        var lookup = table.LookupList[lookupIndex];
        var savedProps = _lookupProps;
        _lookupProps = PropsOf(lookup);
        _nestingLeft--;
        var applied = ApplySubtables(lookup);
        _nestingLeft++;
        _lookupProps = savedProps;
        return applied;
    }

    private static uint PropsOf(ILookupTable table)
    {
        uint props = table.LookupFlag;
        if ((props & (uint)LookupFlags.UseMarkFilteringSet) != 0)
        {
            props |= (uint)table.MarkFilteringSet << 16;
        }

        return props;
    }

    private static bool IsReverse(ILookupTable table)
    {
        return table.SubTables.Length > 0 && table.SubTables[0] is GSUBLookupSubTable
        {
            Type: GSUBLookupType.ReverseChainingContextSingle
        };
    }

    private bool CheckGlyphProperty(in GlyphInfo info, uint lookupProps)
    {
        uint props = info.Props;
        if ((props & lookupProps & IgnoreFlags) != 0)
        {
            return false;
        }

        if ((props & GlyphProps.Mark) == 0)
        {
            return true;
        }

        if ((lookupProps & (uint)LookupFlags.UseMarkFilteringSet) != 0)
        {
            return _layout.IsInMarkSet((int)(lookupProps >> 16), info.Glyph);
        }

        var attachmentType = lookupProps & (uint)LookupFlags.MarkAttachmentTypeMask;
        if (attachmentType != 0)
        {
            return attachmentType == (props & GlyphProps.MarkAttachmentTypeMask);
        }

        return true;
    }

    private SkipResult MaySkip(in GlyphInfo info, uint lookupProps, bool ignoreZwnj, bool ignoreZwj)
    {
        if (!CheckGlyphProperty(info, lookupProps))
        {
            return SkipResult.Yes;
        }

        if (info.IsDefaultIgnorable && (ignoreZwnj || !info.IsZwnj) && (ignoreZwj || !info.IsZwj)
            && (!_gsub || (info.Flags & UnicodeFlags.Hidden) == 0))
        {
            return SkipResult.Maybe;
        }

        return SkipResult.No;
    }

    private static MatchResult MayMatch(in GlyphInfo info, uint mask, Func<int, GlyphInfo, bool> match, int item)
    {
        if ((info.Mask & mask) == 0)
        {
            return MatchResult.No;
        }

        if (match == null)
        {
            return MatchResult.Maybe;
        }

        return match(item, info) ? MatchResult.Yes : MatchResult.No;
    }

    private bool Step(ref int index, int direction, uint mask, uint lookupProps, bool ignoreZwnj, bool ignoreZwj,
        Func<int, GlyphInfo, bool> match, int item)
    {
        while (true)
        {
            index += direction;
            if (index < 0 || index >= _buffer.Length)
            {
                return false;
            }

            ref var info = ref _buffer.Info[index];
            var skip = MaySkip(info, lookupProps, ignoreZwnj, ignoreZwj);
            if (skip == SkipResult.Yes)
            {
                continue;
            }

            var matched = MayMatch(info, mask, match, item);
            if (matched == MatchResult.Yes || (matched == MatchResult.Maybe && skip == SkipResult.No))
            {
                return true;
            }

            if (skip == SkipResult.No)
            {
                return false;
            }
        }
    }

    private bool NextInput(ref int index, Func<int, GlyphInfo, bool> match, int item)
    {
        return Step(ref index, 1, _lookupMask, _lookupProps, !_gsub, _autoZwj, match, item);
    }

    private bool MatchInput(int count, Func<int, GlyphInfo, bool> match, int[] positions, out int end,
        out int totalComponents)
    {
        end = _index;
        totalComponents = 0;
        if (count > MaxContextLength)
        {
            return false;
        }

        var first = _buffer.Info[_index];
        var firstLigId = first.LigId;
        var firstLigComp = first.LigComp;
        int? baseMaySkip = null;
        var index = _index;
        for (var i = 1; i < count; i++)
        {
            if (!NextInput(ref index, match, i))
            {
                return false;
            }

            positions[i] = index;
            var info = _buffer.Info[index];
            if (firstLigId != 0 && firstLigComp != 0)
            {
                if (firstLigId != info.LigId || firstLigComp != info.LigComp)
                {
                    baseMaySkip ??= LigatureBaseMaySkip(firstLigId) ? 1 : 0;
                    if (baseMaySkip == 0)
                    {
                        return false;
                    }
                }
            }
            else if (info.LigId != 0 && info.LigComp != 0 && info.LigId != firstLigId)
            {
                return false;
            }

            totalComponents += info.LigNumComps;
        }

        end = index + 1;
        totalComponents += first.LigNumComps;
        positions[0] = _index;
        return true;
    }

    private bool LigatureBaseMaySkip(int ligId)
    {
        for (var j = _index - 1; j >= 0 && _buffer.Info[j].LigId == ligId; j--)
        {
            if (_buffer.Info[j].LigComp == 0)
            {
                return MaySkip(_buffer.Info[j], _lookupProps, !_gsub, _autoZwj) == SkipResult.Yes;
            }
        }

        return false;
    }

    private bool MatchBacktrack(int count, Func<int, GlyphInfo, bool> match)
    {
        var index = _index;
        for (var i = 0; i < count; i++)
        {
            if (!Step(ref index, -1, uint.MaxValue, _lookupProps, !_gsub || _autoZwnj, true, match, i))
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchLookahead(int count, Func<int, GlyphInfo, bool> match, int start)
    {
        var index = start - 1;
        for (var i = 0; i < count; i++)
        {
            if (!Step(ref index, 1, uint.MaxValue, _lookupProps, !_gsub || _autoZwnj, true, match, i))
            {
                return false;
            }
        }

        return true;
    }

    private bool ApplyContext(int inputCount, Func<int, GlyphInfo, bool> input, int backtrackCount,
        Func<int, GlyphInfo, bool> backtrack, int lookaheadCount, Func<int, GlyphInfo, bool> lookahead,
        SequenceLookupRecord[] records)
    {
        var positions = PositionsForLevel();
        if (!MatchInput(inputCount, input, positions, out var end, out _)
            || !MatchLookahead(lookaheadCount, lookahead, end)
            || !MatchBacktrack(backtrackCount, backtrack))
        {
            return false;
        }

        ApplyNested(positions, inputCount, records, end);
        return true;
    }

    private int[] PositionsForLevel()
    {
        var level = MaxNestingLevel - _nestingLeft;
        return _positions[level] ??= new int[MaxContextLength];
    }

    private void ApplyNested(int[] positions, int count, SequenceLookupRecord[] records, int end)
    {
        foreach (var record in records)
        {
            int sequenceIndex = record.SequenceIndex;
            if (sequenceIndex >= count)
            {
                continue;
            }

            var originalLength = _buffer.Length;
            if (positions[sequenceIndex] >= originalLength)
            {
                continue;
            }

            _index = positions[sequenceIndex];
            if (!Recurse(record.LookupListIndex))
            {
                continue;
            }

            var delta = _buffer.Length - originalLength;
            if (delta == 0)
            {
                continue;
            }

            end += delta;
            if (end < positions[sequenceIndex])
            {
                delta += positions[sequenceIndex] - end;
                end = positions[sequenceIndex];
            }

            var next = sequenceIndex + 1;
            if (delta > 0)
            {
                if (delta + count > MaxContextLength)
                {
                    break;
                }
            }
            else
            {
                delta = Math.Max(delta, next - count);
                next -= delta;
            }

            Array.Copy(positions, next, positions, next + delta, count - next);
            next += delta;
            count += delta;
            for (var j = sequenceIndex + 1; j < next; j++)
            {
                positions[j] = positions[j - 1] + 1;
            }

            for (; next < count; next++)
            {
                positions[next] += delta;
            }
        }

        _index = end;
    }
}
