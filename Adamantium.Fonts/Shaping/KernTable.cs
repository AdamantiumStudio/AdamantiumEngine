using Adamantium.Fonts.Parsers;
using Adamantium.Fonts.Tables;

namespace Adamantium.Fonts.Shaping;

// The 'kern' table as HarfBuzz applies it (its kerx machinery): Microsoft's subtables of pairs (format 0) and classes
// (format 2), and Apple's besides of a state machine (format 1) and compact classes (format 3).
internal static class KernTable
{
    private const int MostOps = 16384;
    private const int OpsPerGlyph = 64;

    // Each subtable of the text's direction in turn; the first one across the direction chains every glyph to the one
    // before it, so a glyph it moves carries the rest of the line along. HarfBuzz carries offsets along the chains only
    // once something was attached: the chains stay idle if nothing was before them and nothing moved a glyph across.
    public static void Apply(GlyphBuffer buffer, OpenTypeLayout layout, ShapePlan plan, bool rightToLeft)
    {
        var placements = buffer.Placements;
        var chained = false;
        var attached = false;
        foreach (var subtable in layout.KernSubtables)
        {
            if (subtable.Variation || subtable.Horizontal == plan.Vertical)
            {
                continue;
            }

            if (subtable.CrossStream && !chained)
            {
                chained = true;
                for (var i = 0; i < buffer.Length; i++)
                {
                    attached |= placements[i].AttachChain != 0;
                    placements[i].AttachType = GlyphPlacement.AttachCursive;
                    placements[i].AttachChain = rightToLeft ? 1 : -1;
                }
            }

            switch (subtable.Format)
            {
                case 0:
                case 2:
                case 3 when subtable.Apple:
                    if (plan.KernMask != 0)
                    {
                        attached |= Pairs(buffer, subtable, plan.KernMask, plan.Vertical, rightToLeft);
                    }

                    break;
                case 1 when subtable.Apple:
                    if (plan.KernMask != 0 || subtable.CrossStream)
                    {
                        attached |= StateMachine(buffer, subtable, plan.KernMask, plan.Vertical, rightToLeft);
                    }

                    break;
            }
        }

        if (chained && !attached)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                placements[i].AttachChain = 0;
            }
        }
    }

    // One subtable's pairs, left to right on the line; marks and visible ignorables between the two are passed over.
    // Whether a pair moved a glyph across the line.
    private static bool Pairs(GlyphBuffer buffer, KerningSubtable subtable, uint kernMask, bool vertical, bool rightToLeft)
    {
        var info = buffer.Info;
        var placements = buffer.Placements;
        var last = buffer.Length - 1;
        var across = false;
        var index = 0;
        while (index < buffer.Length)
        {
            var i = rightToLeft ? last - index : index;
            if ((info[i].Mask & kernMask) == 0)
            {
                index++;
                continue;
            }

            var next = index + 1;
            while (next < buffer.Length && PassedOver(info[rightToLeft ? last - next : next]))
            {
                next++;
            }

            var j = rightToLeft ? last - next : next;
            if (next >= buffer.Length || (info[j].Mask & kernMask) == 0)
            {
                index++;
                continue;
            }

            var kern = Value(subtable, info[i].Glyph, info[j].Glyph);
            if (kern != 0)
            {
                var first = kern >> 1;
                var second = kern - first;
                across |= subtable.CrossStream;
                if (subtable.CrossStream && vertical)
                {
                    placements[j].XOffset = kern;
                }
                else if (subtable.CrossStream)
                {
                    placements[j].YOffset = kern;
                }
                else if (vertical)
                {
                    placements[i].YAdvance += first;
                    placements[j].YAdvance += second;
                    placements[j].YOffset += second;
                }
                else
                {
                    placements[i].XAdvance += first;
                    placements[j].XAdvance += second;
                    placements[j].XOffset += second;
                }
            }

            index = next;
        }

        return across;
    }

    private static bool PassedOver(in GlyphInfo info) => info.IsMark || info.IsDefaultIgnorableAndNotHidden;

    private static int Value(KerningSubtable subtable, uint left, uint right)
    {
        switch (subtable.Format)
        {
            case 0:
                return subtable.KerningValues.TryGetValue(SfntParser.GenerateKerningKey((ushort)left, (ushort)right),
                    out var value)
                    ? value
                    : 0;
            case 2:
                return ClassValue(subtable, left, right);
            default:
                return CompactClassValue(subtable, left, right);
        }
    }

    // Format 2: the left class is the byte offset of its row, the right one of its column; the value stands at their
    // sum from the start of the subtable, within the array.
    private static int ClassValue(KerningSubtable subtable, uint left, uint right)
    {
        var data = subtable.Data;
        var at = subtable.HeaderSize;
        if (at + 8 > data.Length)
        {
            return 0;
        }

        var offset = WideClass(data, U16(data, at + 2), left) + WideClass(data, U16(data, at + 4), right);
        var array = U16(data, at + 6);
        if (offset < array)
        {
            return 0;
        }

        offset = array + (offset - array) / 2 * 2;
        return offset + 2 <= data.Length ? I16(data, offset) : 0;
    }

    private static int WideClass(byte[] data, int table, uint glyph)
    {
        if (table + 4 > data.Length)
        {
            return 0;
        }

        var index = glyph - (uint)U16(data, table);
        return index < U16(data, table + 2) && table + 4 + 2 * index + 2 <= data.Length
            ? U16(data, table + 4 + 2 * (int)index)
            : 0;
    }

    // Apple's format 3: a class for each glyph on either side, an index into the values for each pair of classes.
    private static int CompactClassValue(KerningSubtable subtable, uint left, uint right)
    {
        var data = subtable.Data;
        var at = subtable.HeaderSize;
        if (at + 6 > data.Length)
        {
            return 0;
        }

        int glyphCount = U16(data, at);
        int valueCount = data[at + 2];
        int leftClasses = data[at + 3];
        int rightClasses = data[at + 4];
        var values = at + 6;
        var leftClass = values + 2 * valueCount;
        var rightClass = leftClass + glyphCount;
        var indices = rightClass + glyphCount;
        var l = left < glyphCount ? Byte(data, leftClass + (int)left) : 0;
        var r = right < glyphCount ? Byte(data, rightClass + (int)right) : 0;
        if (l >= leftClasses || r >= rightClasses)
        {
            return 0;
        }

        var index = Byte(data, indices + l * rightClasses + r);
        return index < valueCount && values + 2 * index + 2 <= data.Length ? I16(data, values + 2 * index) : 0;
    }

    // Apple's format 1, as HarfBuzz drives it: glyphs pushed on a stack of eight as the machine passes them; an action
    // pops them, each taking the next value of a list ended by an odd one. Across the line a value moves a chained glyph,
    // and -0x8000 takes it off its chain; along it, it moves the glyph and its advance.
    private static bool StateMachine(GlyphBuffer buffer, KerningSubtable subtable, uint kernMask, bool vertical,
        bool rightToLeft)
    {
        var data = subtable.Data;
        var machine = subtable.HeaderSize;
        if (machine + 10 > data.Length)
        {
            return false;
        }

        int classes = U16(data, machine);
        var classTable = machine + U16(data, machine + 2);
        int stateArray = U16(data, machine + 4);
        var entryTable = machine + U16(data, machine + 6);
        int kernAction = U16(data, machine + 8);
        if (classes < 4)
        {
            return false;
        }

        var info = buffer.Info;
        var placements = buffer.Placements;
        var length = buffer.Length;
        var stack = new int[8];
        var depth = 0;
        var state = 0;
        var attached = false;
        var ops = System.Math.Max(length * OpsPerGlyph, MostOps);
        for (var index = 0;;)
        {
            var glyphClass = index < length ? Class(data, classTable, info[rightToLeft ? length - 1 - index : index].Glyph) : 0;
            if (glyphClass >= classes)
            {
                glyphClass = 1;
            }

            var row = machine + stateArray + state * classes + glyphClass;
            if (row < 0 || row >= data.Length || entryTable + 4 * data[row] + 4 > data.Length)
            {
                return attached;
            }

            var entry = entryTable + 4 * data[row];
            var newState = U16(data, entry);
            var flags = U16(data, entry + 2);
            if ((flags & 0x8000) != 0)
            {
                if (depth < stack.Length)
                {
                    stack[depth++] = index;
                }
                else
                {
                    depth = 0;
                }
            }

            var action = flags & 0x3FFF;
            if (action != 0 && depth > 0)
            {
                var value = machine + kernAction + (action - kernAction) / 2 * 2;
                if (action < kernAction || value + 2 * depth > data.Length)
                {
                    depth = 0;
                }
                else
                {
                    var lastValue = false;
                    while (!lastValue && depth > 0)
                    {
                        var at = stack[--depth];
                        int v = I16(data, value);
                        value += 2;
                        if (at >= length)
                        {
                            continue;
                        }

                        lastValue = (v & 1) != 0;
                        v &= ~1;
                        var glyph = rightToLeft ? length - 1 - at : at;
                        ref var placement = ref placements[glyph];
                        if (subtable.CrossStream && v == -0x8000)
                        {
                            placement.AttachType = 0;
                            placement.AttachChain = 0;
                            if (vertical)
                            {
                                placement.XOffset = 0;
                            }
                            else
                            {
                                placement.YOffset = 0;
                            }
                        }
                        else if (subtable.CrossStream && placement.AttachType != 0)
                        {
                            if (vertical)
                            {
                                placement.XOffset += v;
                            }
                            else
                            {
                                placement.YOffset += v;
                            }

                            attached = true;
                        }
                        else if (!subtable.CrossStream && (info[glyph].Mask & kernMask) != 0)
                        {
                            if (vertical)
                            {
                                placement.YAdvance += v;
                                placement.YOffset += v;
                            }
                            else
                            {
                                placement.XAdvance += v;
                                placement.XOffset += v;
                            }
                        }
                    }
                }
            }

            state = (newState - stateArray) / classes;
            if (index == length)
            {
                return attached;
            }

            if ((flags & 0x4000) == 0 || ops-- <= 0)
            {
                index++;
            }
        }
    }

    // The glyph's class: 1 outside the table, 2 for a deleted glyph.
    private static int Class(byte[] data, int table, uint glyph)
    {
        if (glyph == 0xFFFF)
        {
            return 2;
        }

        if (table + 4 > data.Length)
        {
            return 1;
        }

        var index = glyph - (uint)U16(data, table);
        return index < U16(data, table + 2) && table + 4 + index < data.Length ? data[table + 4 + (int)index] : 1;
    }

    private static int Byte(byte[] data, int at) => at >= 0 && at < data.Length ? data[at] : 0;

    private static int U16(byte[] data, int at) => (data[at] << 8) | data[at + 1];

    private static int I16(byte[] data, int at) => (short)U16(data, at);
}
