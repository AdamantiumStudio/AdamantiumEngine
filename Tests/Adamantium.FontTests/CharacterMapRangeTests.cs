using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.Fonts.Tables.CMAP;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class CharacterMapRangeTests
{
    [Test]
    public void GroupsOutOfOrderAreAllMapped()
    {
        var map = new CharacterMapFormat13
        {
            Groups =
            [
                new ConstantMapGroup { StartCharCode = 0x41, EndCharCode = 0x43, GlyphId = 7 },
                new ConstantMapGroup { StartCharCode = 0x10, EndCharCode = 0x20, GlyphId = 5 },
            ],
        };
        var mapping = new Dictionary<uint, uint>();

        map.GetUnicodeToGlyphMappings(mapping);

        Assert.That(mapping, Has.Count.EqualTo(3 + 17));
        Assert.That(mapping[0x42], Is.EqualTo(7));
        Assert.That(mapping[0x10], Is.EqualTo(5));
    }

    [Test]
    public void RangesToTheTypesMaximumStopAtTheLastCodePointAndVisitEachCodeOnce()
    {
        var groups = new ConstantMapGroup[2000];
        for (var i = 0; i < groups.Length; i++)
        {
            groups[i] = new ConstantMapGroup { StartCharCode = (uint)i, EndCharCode = uint.MaxValue, GlyphId = 1 };
        }

        var map = new CharacterMapFormat13 { Groups = groups };
        var mapping = new Dictionary<uint, uint>();
        var codes = new List<uint>();
        var watch = Stopwatch.StartNew();

        map.GetUnicodeToGlyphMappings(mapping);
        map.CollectUnicodeChars(codes);

        Assert.That(mapping, Has.Count.EqualTo(0x110000));
        Assert.That(codes, Has.Count.EqualTo(0x110000));
        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10));
    }

    [Test]
    public void SegmentEndingAtFFFFEnds()
    {
        var map = new CharacterMapFormat4
        {
            StartCode = [0x41, 0xFFFF],
            EndCode = [0x42, 0xFFFF],
            IdDelta = [1, 1],
            IdRangeOffsets = [0, 0],
            GlyphIdArray = [],
        };
        var codes = new List<uint>();

        map.CollectUnicodeChars(codes);

        Assert.That(codes, Is.EqualTo(new uint[] { 0x41, 0x42, 0xFFFF }));
    }
}
