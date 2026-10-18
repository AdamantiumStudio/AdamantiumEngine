using System.Globalization;
using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Shaping;

internal static class Positioner
{
    public static void Position(IFont font, GlyphBuffer buffer, ShapePlan plan, LookupApplier applier, bool rightToLeft,
        bool componentsRightToLeft)
    {
        buffer.ClearPlacements();
        var info = buffer.Info;
        var placements = buffer.Placements;
        for (var i = 0; i < buffer.Length; i++)
        {
            var glyph = info[i].Glyph;
            if (plan.Vertical)
            {
                placements[i].YAdvance = -font.GetAdvanceHeight(glyph);
                placements[i].XOffset = -(font.GetAdvanceWidth(glyph) / 2);
                placements[i].YOffset = -font.GetVerticalOriginY(glyph);
            }
            else
            {
                placements[i].XAdvance = font.GetAdvanceWidth(glyph);
            }
        }

        if (buffer.HasSpaceFallback)
        {
            FallbackSpaces(font, buffer, plan.Vertical);
        }

        if (plan.ApplyGpos)
        {
            applier.ApplyGpos(plan);
        }

        if (plan.ApplyKernTable)
        {
            KernTable.Apply(buffer, font.Layout, plan, rightToLeft);
        }

        var adjustOffsets = plan.AdjustMarksWhenZeroing && !rightToLeft;
        for (var i = 0; plan.ZeroMarks && i < buffer.Length; i++)
        {
            if (!info[i].IsMark)
            {
                continue;
            }

            if (adjustOffsets)
            {
                placements[i].XOffset -= placements[i].XAdvance;
                placements[i].YOffset -= placements[i].YAdvance;
            }

            placements[i].XAdvance = 0;
            placements[i].YAdvance = 0;
        }

        if (buffer.HasDefaultIgnorables)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (info[i].IsDefaultIgnorable)
                {
                    placements[i].XAdvance = placements[i].YAdvance = 0;
                    placements[i].XOffset = placements[i].YOffset = 0;
                }
            }
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            PropagateAttachment(placements, buffer.Length, i, 64, rightToLeft, plan.Vertical);
        }

        if (plan.FallbackMarkPositioning)
        {
            FallbackMarks(font, buffer, plan, rightToLeft, componentsRightToLeft);
        }
    }

    // The classes of marks a font with no positioning has placed around their base by HarfBuzz's fallback: where on
    // the base a mark goes, by its combining class.
    public static void RecategorizeMarks(GlyphBuffer buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            ref var info = ref buffer.Info[i];
            if (info.Category == UnicodeCategory.NonSpacingMark)
            {
                info.CombiningClass = Recategorized(info.Codepoint, info.CombiningClass);
            }
        }
    }

    // HarfBuzz's modified classes: Hebrew 10-26, Arabic and Syriac 27-36, Thai and Lao 3, 107, 118 and 122, Tibetan
    // 129-132; Thai and Lao marks of no class by the character. Their own shaper places no marks, but a run of another
    // script set by hand can hold them.
    private static byte Recategorized(int codepoint, byte combiningClass)
    {
        if (combiningClass >= 200)
        {
            return combiningClass;
        }

        if ((codepoint & ~0xFF) == 0x0E00)
        {
            if (combiningClass == 0)
            {
                combiningClass = codepoint switch
                {
                    0x0E31 or 0x0E34 or 0x0E35 or 0x0E36 or 0x0E37 or 0x0E47 or 0x0E4C or 0x0E4D or 0x0E4E => AboveRight,
                    0x0EB1 or 0x0EB4 or 0x0EB5 or 0x0EB6 or 0x0EB7 or 0x0EBB or 0x0ECC or 0x0ECD => Above,
                    0x0EBC => Below,
                    _ => combiningClass,
                };
            }
            else if (codepoint == 0x0E3A)
            {
                combiningClass = BelowRight;
            }
        }

        return Recategorized(combiningClass);
    }

    private static byte Recategorized(byte combiningClass) => combiningClass switch
    {
        >= 200 => combiningClass,
        3 => BelowRight,
        107 => AboveRight,
        118 => Below,
        122 => Above,
        22 or 15 or 16 or 17 or 23 or 18 or 19 or 20 or 21 or 24 or 25 => Below,
        13 => AttachedAbove,
        10 => AboveRight,
        11 or 14 => AboveLeft,
        26 => Above,
        28 or 29 or 31 or 32 or 27 or 34 or 35 or 36 => Above,
        30 or 33 => Below,
        129 or 131 => Below,
        132 => Above,
        _ => combiningClass,
    };

    private const byte AttachedBelowLeft = 200;
    private const byte AttachedBelow = 202;
    private const byte AttachedAbove = 214;
    private const byte AttachedAboveRight = 216;
    private const byte BelowLeft = 218;
    private const byte Below = 220;
    private const byte BelowRight = 222;
    private const byte AboveLeft = 228;
    private const byte Above = 230;
    private const byte AboveRight = 232;
    private const byte DoubleBelow = 233;
    private const byte DoubleAbove = 234;

    private struct Extents
    {
        public int XBearing;
        public int YBearing;
        public int Width;
        public int Height;
    }

    // As HarfBuzz reads them: from the outline's box, its left side from 'hmtx' for a TrueType glyph, all zero for a
    // glyph with no outline; Y up, the height down from the top.
    private static Extents ExtentsOf(IFont font, uint glyphIndex)
    {
        var glyph = font.GetGlyphByIndex(glyphIndex);
        if (glyph == null || glyph.IsEmpty || !glyph.HasOutlines)
        {
            return default;
        }

        var box = glyph.BoundingRectangle;
        var trueType = glyph.OutlineType == OutlineType.TrueType && font.NormalizedCoordinates is not { Length: > 0 };
        return new Extents
        {
            XBearing = trueType ? font.GetLeftSideBearing(glyphIndex) : box.Left,
            YBearing = box.Bottom,
            Width = box.Width,
            Height = -box.Height,
        };
    }

    private static void FallbackMarks(IFont font, GlyphBuffer buffer, ShapePlan plan, bool rightToLeft,
        bool componentsRightToLeft)
    {
        var info = buffer.Info;
        var start = 0;
        for (var i = 1; i < buffer.Length; i++)
        {
            if (!info[i].IsUnicodeMark)
            {
                PositionCluster(start, i);
                start = i;
            }
        }

        PositionCluster(start, buffer.Length);

        void PositionCluster(int clusterStart, int end)
        {
            if (end - clusterStart < 2)
            {
                return;
            }

            for (var i = clusterStart; i < end; i++)
            {
                if (info[i].IsUnicodeMark)
                {
                    continue;
                }

                var j = i + 1;
                while (j < end && info[j].IsUnicodeMark)
                {
                    j++;
                }

                PositionAroundBase(i, j);
                i = j - 1;
            }
        }

        void PositionAroundBase(int baseIndex, int end)
        {
            var placements = buffer.Placements;
            var baseExtents = ExtentsOf(font, info[baseIndex].Glyph);
            baseExtents.YBearing += placements[baseIndex].YOffset;
            // The advance across, not the ink: it works for glyphs with none.
            baseExtents.XBearing = 0;
            baseExtents.Width = font.GetAdvanceWidth(info[baseIndex].Glyph);

            var ligatureId = info[baseIndex].LigId;
            var components = info[baseIndex].LigNumComps;
            int xOffset = 0, yOffset = 0;
            if (!rightToLeft)
            {
                xOffset -= placements[baseIndex].XAdvance;
                yOffset -= placements[baseIndex].YAdvance;
            }

            var componentExtents = baseExtents;
            var lastComponent = -1;
            var lastClass = 255;
            var clusterExtents = baseExtents;
            for (var i = baseIndex + 1; i < end; i++)
            {
                if (info[i].CombiningClass == 0)
                {
                    var sign = rightToLeft ? 1 : -1;
                    xOffset += sign * placements[i].XAdvance;
                    yOffset += sign * placements[i].YAdvance;
                    continue;
                }

                if (components > 1)
                {
                    var component = info[i].LigComp - 1;
                    if (ligatureId == 0 || ligatureId != info[i].LigId || component >= components)
                    {
                        component = components - 1;
                    }

                    if (lastComponent != component)
                    {
                        lastComponent = component;
                        lastClass = 255;
                        componentExtents = baseExtents;
                        componentExtents.XBearing += (componentsRightToLeft ? components - 1 - component : component)
                            * componentExtents.Width / components;
                        componentExtents.Width /= components;
                    }
                }

                var combiningClass = info[i].CombiningClass;
                if (lastClass != combiningClass)
                {
                    lastClass = combiningClass;
                    clusterExtents = componentExtents;
                }

                PositionMark(ref clusterExtents, i, combiningClass);
                placements[i].XAdvance = 0;
                placements[i].YAdvance = 0;
                placements[i].XOffset += xOffset;
                placements[i].YOffset += yOffset;
            }
        }

        void PositionMark(ref Extents baseExtents, int i, byte combiningClass)
        {
            var mark = ExtentsOf(font, info[i].Glyph);
            var gap = font.UnitsPerEm / 16;
            ref var placement = ref buffer.Placements[i];
            placement.XOffset = 0;
            placement.YOffset = 0;

            // Left and right marks are not moved.
            switch (combiningClass)
            {
                case DoubleBelow or DoubleAbove when !plan.Vertical:
                    placement.XOffset += rightToLeft
                        ? baseExtents.XBearing - mark.Width / 2 - mark.XBearing
                        : baseExtents.XBearing + baseExtents.Width - mark.Width / 2 - mark.XBearing;
                    break;
                case AttachedBelowLeft or BelowLeft or AboveLeft:
                    placement.XOffset += baseExtents.XBearing - mark.XBearing;
                    break;
                case AttachedAboveRight or BelowRight or AboveRight:
                    placement.XOffset += baseExtents.XBearing + baseExtents.Width - mark.Width - mark.XBearing;
                    break;
                default:
                    placement.XOffset += baseExtents.XBearing + (baseExtents.Width - mark.Width) / 2 - mark.XBearing;
                    break;
            }

            switch (combiningClass)
            {
                case DoubleBelow or BelowLeft or Below or BelowRight or AttachedBelowLeft or AttachedBelow:
                    if (combiningClass is not (AttachedBelowLeft or AttachedBelow))
                    {
                        baseExtents.Height -= gap;
                    }

                    placement.YOffset = baseExtents.YBearing + baseExtents.Height - mark.YBearing;
                    // Never shift a mark below up.
                    if ((gap > 0) == (placement.YOffset > 0))
                    {
                        baseExtents.Height -= placement.YOffset;
                        placement.YOffset = 0;
                    }

                    baseExtents.Height += mark.Height;
                    break;
                case DoubleAbove or AboveLeft or Above or AboveRight or AttachedAbove or AttachedAboveRight:
                    if (combiningClass is not (AttachedAbove or AttachedAboveRight))
                    {
                        baseExtents.YBearing += gap;
                        baseExtents.Height -= gap;
                    }

                    placement.YOffset = baseExtents.YBearing - (mark.YBearing + mark.Height);
                    // Nor one above too far down.
                    if ((gap > 0) != (placement.YOffset > 0))
                    {
                        var correction = -placement.YOffset / 2;
                        baseExtents.YBearing += correction;
                        baseExtents.Height -= correction;
                        placement.YOffset += correction;
                    }

                    baseExtents.YBearing -= mark.Height;
                    baseExtents.Height += mark.Height;
                    break;
            }
        }
    }

    private static void FallbackSpaces(IFont font, GlyphBuffer buffer, bool vertical)
    {
        var info = buffer.Info;
        var placements = buffer.Placements;
        int em = font.UnitsPerEm;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (info[i].Space == SpaceKind.NotSpace || info[i].IsLigated)
            {
                continue;
            }

            switch (info[i].Space)
            {
                case SpaceKind.Em:
                case SpaceKind.Em2:
                case SpaceKind.Em3:
                case SpaceKind.Em4:
                case SpaceKind.Em5:
                case SpaceKind.Em6:
                case SpaceKind.Em16:
                    var divisor = (int)info[i].Space;
                    SetAdvance(i, (em + divisor / 2) / divisor);
                    break;
                case SpaceKind.FourEm18:
                    SetAdvance(i, em * 4 / 18);
                    break;
                case SpaceKind.Figure:
                    for (var digit = '0'; digit <= '9'; digit++)
                    {
                        if (font.TryGetGlyphIndex(digit, out var glyph))
                        {
                            SetAdvance(i, vertical ? font.GetAdvanceHeight(glyph) : font.GetAdvanceWidth(glyph));
                            break;
                        }
                    }

                    break;
                case SpaceKind.Punctuation:
                    if (font.TryGetGlyphIndex('.', out var period) || font.TryGetGlyphIndex(',', out period))
                    {
                        SetAdvance(i, vertical ? font.GetAdvanceHeight(period) : font.GetAdvanceWidth(period));
                    }

                    break;
                case SpaceKind.Narrow:
                    if (vertical)
                    {
                        placements[i].YAdvance /= 2;
                    }
                    else
                    {
                        placements[i].XAdvance /= 2;
                    }

                    break;
            }
        }

        void SetAdvance(int index, int advance)
        {
            if (vertical)
            {
                placements[index].YAdvance = -advance;
            }
            else
            {
                placements[index].XAdvance = advance;
            }
        }
    }

    private static void PropagateAttachment(GlyphPlacement[] placements, int length, int i, int nesting, bool rightToLeft,
        bool vertical)
    {
        var chain = placements[i].AttachChain;
        var type = placements[i].AttachType;
        if (chain == 0)
        {
            return;
        }

        placements[i].AttachChain = 0;
        var j = i + chain;
        if (j < 0 || j >= length || nesting == 0)
        {
            return;
        }

        PropagateAttachment(placements, length, j, nesting - 1, rightToLeft, vertical);
        if ((type & GlyphPlacement.AttachCursive) != 0 && vertical)
        {
            placements[i].XOffset += placements[j].XOffset;
            return;
        }

        if ((type & GlyphPlacement.AttachCursive) != 0)
        {
            placements[i].YOffset += placements[j].YOffset;
            return;
        }

        placements[i].XOffset += placements[j].XOffset;
        placements[i].YOffset += placements[j].YOffset;
        if (rightToLeft)
        {
            for (var k = j + 1; k <= i; k++)
            {
                placements[i].XOffset += placements[k].XAdvance;
                placements[i].YOffset += placements[k].YAdvance;
            }

            return;
        }

        for (var k = j; k < i; k++)
        {
            placements[i].XOffset -= placements[k].XAdvance;
            placements[i].YOffset -= placements[k].YAdvance;
        }
    }
}
