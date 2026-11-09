using System.Diagnostics;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using NUnit.Framework;

namespace Adamantium.FontTests.Cff;

/// <summary>Bare CFF fonts built to hang, blow up or crash a reader that trusts them.</summary>
public class HostileCffTests
{
    [Test]
    public void SmallestFontWithAnEmptyStringIndexLoads()
    {
        var typeface = Typeface.LoadCff(new CffFontBuilder().Build());

        Assert.That(typeface.GlyphCount, Is.EqualTo(2));
        Assert.That(typeface.GetGlyphByIndex(1, out var space) && space.Name == "space", "ISOAdobe names glyph 1");
    }

    [Test]
    public void GlobalSubroutineCallingItselfLeavesItsGlyphInvalid()
    {
        var builder = new CffFontBuilder();
        builder.GlobalSubrs.Add([32, 29]);
        builder.CharStrings[1] = [32, 29];

        var typeface = Typeface.LoadCff(builder.Build());
        var watch = Stopwatch.StartNew();
        typeface.GetGlyphByIndex(1, out var looping);

        Assert.That(looping.IsInvalid, Is.True);
        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
        typeface.GetGlyphByIndex(0, out var notdef);
        Assert.That(notdef.IsInvalid, Is.False);
    }

    [Test]
    public void ThousandsOfGlyphsCallingASelfCallingSubroutineStopAtTheFontsBudget()
    {
        var builder = new CffFontBuilder();
        builder.GlobalSubrs.Add([32, 29]);
        builder.CharStrings.Clear();
        builder.CharStrings.Add([14]);
        for (var i = 0; i < 2000; i++)
        {
            builder.CharStrings.Add([32, 29]);
        }

        var typeface = Typeface.LoadCff(builder.Build());
        var watch = Stopwatch.StartNew();
        foreach (var glyph in typeface.Glyphs)
        {
            _ = glyph.BoundingRectangle;
        }

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
        Assert.That(typeface.Glyphs.Skip(1).All(g => g.IsInvalid));
    }

    [Test]
    public void LongPrivateDictReadsInLinearTime()
    {
        var builder = new CffFontBuilder { PrivateDict = Enumerable.Repeat((byte)139, 1 << 20).ToArray() };

        var watch = Stopwatch.StartNew();
        Typeface.LoadCff(builder.Build());

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
    }

    [Test]
    public void ExpertCharsetAndEncodingNameAndPlaceTheGlyphs()
    {
        var builder = new CffFontBuilder { Charset = 1, Encoding = 1 };
        builder.CharStrings.Add([14]);

        var typeface = Typeface.LoadCff(builder.Build());

        typeface.GetGlyphByIndex(2, out var exclamSmall);
        Assert.That(exclamSmall.Name, Is.EqualTo("exclamsmall"));
        Assert.That(typeface.BuiltInEncoding[33], Is.EqualTo(2), "the Expert encoding puts exclamsmall at 33");
        Assert.That(typeface.BuiltInEncoding[32], Is.EqualTo(1));
        Assert.That(typeface.BuiltInEncoding[65], Is.EqualTo(-1));
    }

    [Test]
    public void ExpertSubsetCharsetNamesTheGlyphs()
    {
        var builder = new CffFontBuilder { Charset = 2 };
        builder.CharStrings.Add([14]);

        var typeface = Typeface.LoadCff(builder.Build());

        typeface.GetGlyphByIndex(2, out var dollarOldStyle);
        Assert.That(dollarOldStyle.Name, Is.EqualTo("dollaroldstyle"));
    }

    [TestCase(new byte[] { 0, 2, 1, 1, 3, 2, 0, 0, 0 }, TestName = "Offsets going back")]
    [TestCase(new byte[] { 0, 1, 1, 2, 3, 0, 0 }, TestName = "First offset not 1")]
    [TestCase(new byte[] { 0, 1, 5, 1, 2 }, TestName = "Offset size 5")]
    [TestCase(new byte[] { 0, 2, 1, 1, 3, 200, 0, 0 }, TestName = "Data past the end")]
    public void MalformedIndexIsRejected(byte[] index)
    {
        Assert.That(() => new FontStreamReader(index).ReadCffIndex(),
            Throws.TypeOf<InvalidDataException>().Or.TypeOf<EndOfStreamException>());
    }

    [Test]
    public void IndexEntriesShareItsDataOnce()
    {
        var index = new FontStreamReader([0, 3, 1, 1, 3, 3, 5, 1, 2, 3, 4]).ReadCffIndex();

        Assert.That(index.DataByOffset.Select(d => d.Length), Is.EqualTo(new[] { 2, 0, 2 }));
        Assert.That(index.DataByOffset[2], Is.EqualTo(new byte[] { 3, 4 }));
    }

    [Test]
    public void EveryByteChangedGivesAFontOrInvalidData()
    {
        var builder = new CffFontBuilder();
        builder.GlobalSubrs.Add([139, 139, 21, 11]);
        builder.CharStrings[1] = [139, 139, 21, 247, 0, 139, 5, 107, 29, 14];
        var data = builder.Build();
        foreach (var value in new byte[] { 0, 1, 12, 28, 29, 30, 139, 255 })
        {
            for (var at = 0; at < data.Length; at++)
            {
                var changed = (byte[])data.Clone();
                changed[at] = value;
                try
                {
                    foreach (var glyph in Typeface.LoadCff(changed).Glyphs)
                    {
                        _ = glyph.BoundingRectangle;
                    }
                }
                catch (InvalidDataException)
                {
                }
            }
        }
    }
}
