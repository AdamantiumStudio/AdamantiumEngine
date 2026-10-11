using System;
using Adamantium.Fonts.Tables.GPOS;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Shaping;

internal sealed partial class LookupApplier
{
    private bool ApplyPositioning(ILookupSubTable subtable)
    {
        var glyph = (ushort)_buffer.Info[_index].Glyph;
        switch (subtable)
        {
            case SingleAdjustmentPositioningSubTable single:
                var singleIndex = single.Coverage.FindPosition(glyph);
                if (singleIndex < 0)
                {
                    return false;
                }

                ApplyValue(single.ValueRecords[single.Format == 1 ? 0 : singleIndex], ref _buffer.Placements[_index]);
                _index++;
                return true;
            case PairAdjustmentPositioningSubTableFormat1 pair:
                return ApplyPair(pair, glyph);
            case PairAdjustmentPositioningSubTableFormat2 pair:
                return ApplyClassPair(pair, glyph);
            case CursiveAttachmentPositioningSubTable cursive:
                return ApplyCursive(cursive, glyph);
            case MarkToBaseAttachmentPositioningSubTable markToBase:
                return ApplyMarkToBase(markToBase, glyph);
            case MarkToLigatureAttachmentPositioningSubTable markToLigature:
                return ApplyMarkToLigature(markToLigature, glyph);
            case MarkToMarkAttachmentPositioningSubtable markToMark:
                return ApplyMarkToMark(markToMark, glyph);
            case ContextualPositioningSubtableFormat1 context:
                return ApplyGlyphContext(context.Coverage, context.SequenceRuleSets, glyph);
            case ContextualPositioningSubtableFormat2 context:
                return ApplyClassContext(context.Coverage, context.ClassDef, context.ClassSequenceRuleSets, glyph);
            case ContextualPositioningSubtableFormat3 context:
                return ApplyCoverageContext(context.CoverageTables, context.LookupRecords);
            case ChainedContextsPositioningSubtableFormat1 chain:
                return ApplyGlyphChain(chain.Coverage, chain.ChainedSeqRuleSets, glyph);
            case ChainedContextsPositioningSubtableFormat2 chain:
                return ApplyClassChain(chain.Coverage, chain.BacktrackClassDef, chain.InputClassDef,
                    chain.LookaheadClassDef, chain.ChainedSeqRuleSets, glyph);
            case ChainedContextsPositioningSubtableFormat3 chain:
                return ApplyCoverageChain(chain.BacktrackCoverages, chain.InputCoverages, chain.LookaheadCoverages,
                    chain.LookupRecords);
            default:
                return false;
        }
    }

    private void ApplyValue(ValueRecord value, ref GlyphPlacement placement)
    {
        if (value == null)
        {
            return;
        }

        placement.XOffset += value.XPlacement + Delta(value.XPlacementVariation);
        placement.YOffset += value.YPlacement + Delta(value.YPlacementVariation);
        if (Vertical)
        {
            placement.YAdvance -= value.YAdvance + Delta(value.YAdvanceVariation);
        }
        else
        {
            placement.XAdvance += value.XAdvance + Delta(value.XAdvanceVariation);
        }
    }

    private int Delta(int variation)
    {
        var store = _layout.Gdef?.VariationStore;
        if (variation < 0 || store == null || _coordinates == null)
        {
            return 0;
        }

        return (int)Math.Floor(store.GetDelta(variation >> 16, variation & 0xFFFF, _coordinates) + 0.5f);
    }

    private int X(AnchorPointTable anchor) => anchor.XCoordinate + Delta(anchor.XDevice?.VariationIndex ?? -1);

    private int Y(AnchorPointTable anchor) => anchor.YCoordinate + Delta(anchor.YDevice?.VariationIndex ?? -1);

    private bool ApplyPair(PairAdjustmentPositioningSubTableFormat1 pair, ushort glyph)
    {
        var coverageIndex = pair.CoverageTable.FindPosition(glyph);
        if (coverageIndex < 0)
        {
            return false;
        }

        var second = _index;
        if (!NextInput(ref second, null, 1))
        {
            return false;
        }

        if (!pair.PairSetsTables[coverageIndex].FindPairSet((ushort)_buffer.Info[second].Glyph, out var record))
        {
            return false;
        }

        ApplyValue(record.ValueRecord1, ref _buffer.Placements[_index]);
        ApplyValue(record.ValueRecord2, ref _buffer.Placements[second]);
        _index = pair.Value2Format != 0 ? second + 1 : second;
        return true;
    }

    private bool ApplyClassPair(PairAdjustmentPositioningSubTableFormat2 pair, ushort glyph)
    {
        if (pair.CoverageTable.FindPosition(glyph) < 0)
        {
            return false;
        }

        var second = _index;
        if (!NextInput(ref second, null, 1))
        {
            return false;
        }

        var class1 = pair.ClassDef1.GetClassValue(glyph);
        var class2 = pair.ClassDef2.GetClassValue((ushort)_buffer.Info[second].Glyph);
        if (class1 >= pair.Class1Records.Length || class2 >= pair.Class1Records[class1].Class2Records.Length)
        {
            return false;
        }

        var record = pair.Class1Records[class1].Class2Records[class2];
        ApplyValue(record.Value1, ref _buffer.Placements[_index]);
        ApplyValue(record.Value2, ref _buffer.Placements[second]);
        _index = pair.Value2Format != 0 ? second + 1 : second;
        return true;
    }

    private bool ApplyCursive(CursiveAttachmentPositioningSubTable cursive, ushort glyph)
    {
        var thisIndex = cursive.Coverage.FindPosition(glyph);
        if (thisIndex < 0 || cursive.EntryAnchors[thisIndex] == null)
        {
            return false;
        }

        var previous = _index;
        if (!Step(ref previous, -1, _lookupMask, _lookupProps, !_gsub, _autoZwj, null, 0))
        {
            return false;
        }

        var previousIndex = cursive.Coverage.FindPosition((ushort)_buffer.Info[previous].Glyph);
        if (previousIndex < 0 || cursive.ExitAnchors[previousIndex] == null)
        {
            return false;
        }

        var placements = _buffer.Placements;
        var i = previous;
        var j = _index;
        var exit = cursive.ExitAnchors[previousIndex];
        var entry = cursive.EntryAnchors[thisIndex];

        if (RightToLeft)
        {
            var d = X(exit) + placements[i].XOffset;
            placements[i].XAdvance -= d;
            placements[i].XOffset -= d;
            placements[j].XAdvance = X(entry) + placements[j].XOffset;
        }
        else
        {
            placements[i].XAdvance = X(exit) + placements[i].XOffset;
            var d = X(entry) + placements[j].XOffset;
            placements[j].XAdvance -= d;
            placements[j].XOffset -= d;
        }

        var child = i;
        var parent = j;
        var yOffset = Y(entry) - Y(exit);
        if ((_lookupProps & (uint)LookupFlags.RightToLeft) == 0)
        {
            child = j;
            parent = i;
            yOffset = -yOffset;
        }

        ReverseCursiveMinorOffset(placements, child, parent);
        placements[child].AttachType = GlyphPlacement.AttachCursive;
        placements[child].AttachChain = parent - child;
        placements[child].YOffset = yOffset;
        if (placements[parent].AttachChain == -placements[child].AttachChain)
        {
            placements[parent].AttachChain = 0;
            placements[parent].YOffset = 0;
        }

        _index++;
        return true;
    }

    private static void ReverseCursiveMinorOffset(GlyphPlacement[] placements, int i, int newParent)
    {
        var chain = placements[i].AttachChain;
        var type = placements[i].AttachType;
        if (chain == 0 || (type & GlyphPlacement.AttachCursive) == 0)
        {
            return;
        }

        placements[i].AttachChain = 0;
        var j = i + chain;
        if (j == newParent)
        {
            return;
        }

        ReverseCursiveMinorOffset(placements, j, newParent);
        placements[j].YOffset = -placements[i].YOffset;
        placements[j].AttachChain = -chain;
        placements[j].AttachType = type;
    }

    private int FindBase(Func<int, bool> accept)
    {
        if (_lastBaseUntil > _index)
        {
            _lastBaseUntil = 0;
            _lastBase = -1;
        }

        for (var j = _index; j > _lastBaseUntil; j--)
        {
            ref var info = ref _buffer.Info[j - 1];
            var skip = MaySkip(info, (uint)LookupFlags.IgnoreMarks, !_gsub, _autoZwj);
            if (skip == SkipResult.Yes)
            {
                continue;
            }

            var matched = (info.Mask & _lookupMask) != 0 ? MatchResult.Maybe : MatchResult.No;
            if (matched == MatchResult.Maybe && skip == SkipResult.No && accept(j - 1))
            {
                _lastBase = j - 1;
                break;
            }
        }

        _lastBaseUntil = _index;
        return _lastBase;
    }

    private bool ApplyMarkToBase(MarkToBaseAttachmentPositioningSubTable table, ushort glyph)
    {
        var markIndex = table.MarkCoverage.FindPosition(glyph);
        if (markIndex < 0)
        {
            return false;
        }

        var baseIndex = FindMarkBase(table.BaseCoverage);
        if (baseIndex < 0)
        {
            return false;
        }

        var coverageIndex = table.BaseCoverage.FindPosition((ushort)_buffer.Info[baseIndex].Glyph);
        if (coverageIndex < 0)
        {
            return false;
        }

        var markClass = table.MarkArrayTable.GetMarkClass(markIndex);
        var anchors = table.BaseArrayTable.BaseRecords[coverageIndex].Anchors;
        return AttachMark(table.MarkArrayTable.GetAnchorPoint(markIndex), markClass < anchors.Length ? anchors[markClass] : null,
            baseIndex);
    }

    private int FindMarkBase(CoverageTable baseCoverage) =>
        FindBase(j => AcceptsBase(j) || baseCoverage.FindPosition((ushort)_buffer.Info[j].Glyph) >= 0);

    private bool AcceptsBase(int index)
    {
        var info = _buffer.Info;
        return !info[index].IsMultiplied || info[index].LigComp == 0 || index == 0 || info[index - 1].IsMark
               || !info[index - 1].IsMultiplied || info[index].LigId != info[index - 1].LigId
               || info[index].LigComp != info[index - 1].LigComp + 1;
    }

    private bool ApplyMarkToLigature(MarkToLigatureAttachmentPositioningSubTable table, ushort glyph)
    {
        var markIndex = table.MarkCoverage.FindPosition(glyph);
        if (markIndex < 0)
        {
            return false;
        }

        var ligatureIndex = FindBase(_ => true);
        if (ligatureIndex < 0)
        {
            return false;
        }

        var coverageIndex = table.LigatureCoverage.FindPosition((ushort)_buffer.Info[ligatureIndex].Glyph);
        if (coverageIndex < 0)
        {
            return false;
        }

        var components = table.LigatureArrayTable.AttachTables[coverageIndex].ComponentRecords;
        if (components.Length == 0)
        {
            return false;
        }

        var ligId = _buffer.Info[ligatureIndex].LigId;
        var markId = _buffer.Info[_index].LigId;
        var markComponent = _buffer.Info[_index].LigComp;
        var componentIndex = ligId != 0 && ligId == markId && markComponent > 0
            ? Math.Min(components.Length, markComponent) - 1
            : components.Length - 1;

        var markClass = table.MarkArrayTable.GetMarkClass(markIndex);
        var anchors = components[componentIndex].Anchors;
        return AttachMark(table.MarkArrayTable.GetAnchorPoint(markIndex), markClass < anchors.Length ? anchors[markClass] : null,
            ligatureIndex);
    }

    private bool ApplyMarkToMark(MarkToMarkAttachmentPositioningSubtable table, ushort glyph)
    {
        var mark1Index = table.Mark1Coverage.FindPosition(glyph);
        if (mark1Index < 0)
        {
            return false;
        }

        var previous = _index;
        if (!Step(ref previous, -1, _lookupMask, _lookupProps & ~IgnoreFlags, !_gsub, _autoZwj, null, 0))
        {
            return false;
        }

        var info = _buffer.Info;
        if (!info[previous].IsMark)
        {
            return false;
        }

        var id1 = info[_index].LigId;
        var id2 = info[previous].LigId;
        var component1 = info[_index].LigComp;
        var component2 = info[previous].LigComp;
        var good = id1 == id2
            ? id1 == 0 || component1 == component2
            : (id1 > 0 && component1 == 0) || (id2 > 0 && component2 == 0);
        if (!good)
        {
            return false;
        }

        var mark2Index = table.Mark2Coverage.FindPosition((ushort)info[previous].Glyph);
        if (mark2Index < 0)
        {
            return false;
        }

        var markClass = table.Mark1ArrayTable.GetMarkClass(mark1Index);
        var anchors = table.Mark2ArrayTable.Records[mark2Index].Anchors;
        return AttachMark(table.Mark1ArrayTable.GetAnchorPoint(mark1Index), markClass < anchors.Length ? anchors[markClass] : null,
            previous);
    }

    private bool AttachMark(AnchorPointTable markAnchor, AnchorPointTable baseAnchor, int baseIndex)
    {
        if (baseAnchor == null || markAnchor == null)
        {
            return false;
        }

        ref var placement = ref _buffer.Placements[_index];
        placement.XOffset = X(baseAnchor) - X(markAnchor);
        placement.YOffset = Y(baseAnchor) - Y(markAnchor);
        placement.AttachType = GlyphPlacement.AttachMark;
        placement.AttachChain = baseIndex - _index;
        _index++;
        return true;
    }
}
