using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Parsers.CFF;
using Adamantium.Fonts.Parsers.Type1;
using Adamantium.Fonts.Tables.CFF;
using NUnit.Framework;
using static Adamantium.FontTests.Type1.Type1FontBuilder;

namespace Adamantium.FontTests.Type1;

public class Type1FontTests
{
    private static Type1FontBuilder SquareFont()
    {
        var builder = new Type1FontBuilder().WithStandardSubrs();
        builder.Glyphs.Add((".notdef", CharString(0, 250, "hsbw", "endchar")));
        builder.Glyphs.Add(("A", CharString(50, 600, "hsbw", 0, 0, "rmoveto", 500, "vlineto", 500, "hlineto",
            -500, "vlineto", "closepath", "endchar")));
        builder.Glyphs.Add(("acute", CharString(100, 300, "hsbw", 0, 0, "rmoveto", 100, "vlineto", 100, "hlineto",
            -100, "vlineto", "closepath", "endchar")));
        return builder;
    }

    [Test]
    public void FontFileOfPdfGivesNameMatrixWidthsAndStandardEncoding()
    {
        var typeface = Typeface.LoadType1(SquareFont().BuildPdfFontFile());

        Assert.That(typeface.CurrentFont.FullName, Is.EqualTo("TestType1"));
        Assert.That(typeface.CurrentFont.UnitsPerEm, Is.EqualTo(1000));
        Assert.That(typeface.GlyphCount, Is.EqualTo(3));
        Assert.That(typeface.GetGlyphByIndex(0, out var notdef) && notdef.Name == ".notdef");
        Assert.That(typeface.GetGlyphByName("A", out var a));
        Assert.That(a.AdvanceWidth, Is.EqualTo(600));
        Assert.That(a.LeftSideBearing, Is.EqualTo(50));
        Assert.That(typeface.BuiltInEncoding[65], Is.EqualTo((int)a.Index));
        Assert.That(typeface.BuiltInEncoding[194], Is.EqualTo(2), "StandardEncoding puts acute at 194");
        Assert.That(typeface.BuiltInEncoding[66], Is.EqualTo(-1));
    }

    [Test]
    public void SquareIsDrawnFromItsSideBearingPoint()
    {
        var typeface = Typeface.LoadType1(SquareFont().BuildPdfFontFile());
        typeface.GetGlyphByName("A", out var a);

        Assert.That(a.IsInvalid, Is.False);
        Assert.That(a.Outlines.Count, Is.EqualTo(1));
        var points = a.Outlines.First().Points.Select(p => (p.X, p.Y)).Distinct().ToArray();
        Assert.That(points, Is.EqualTo(new[] { (50.0, 0.0), (50.0, 500.0), (550.0, 500.0), (550.0, 0.0) }));
    }

    [Test]
    public void PfaAndPfbReadAsThePdfFormDoes()
    {
        var builder = SquareFont();
        var binary = Typeface.LoadType1(builder.BuildPdfFontFile());

        foreach (var data in new[] { builder.BuildPfa(), builder.BuildPfb() })
        {
            var other = Typeface.LoadType1(data);
            Assert.That(other.Glyphs.Select(g => g.Name), Is.EqualTo(binary.Glyphs.Select(g => g.Name)));
            other.GetGlyphByName("A", out var a);
            Assert.That(a.BoundingRectangle, Is.EqualTo(binary.Glyphs.ElementAt(1).BoundingRectangle));
        }
    }

    [Test]
    public void OwnEncodingArrayMapsCodesToNames()
    {
        var builder = SquareFont();
        builder.Encoding = "256 array 0 1 255 {1 index exch /.notdef put} for dup 66 /A put dup 67 /acute put " +
                           "readonly def";

        var typeface = Typeface.LoadType1(builder.BuildPdfFontFile());

        Assert.That(typeface.BuiltInEncoding[66], Is.EqualTo(1));
        Assert.That(typeface.BuiltInEncoding[67], Is.EqualTo(2));
        Assert.That(typeface.BuiltInEncoding[65], Is.EqualTo(-1), "codes the array leaves unset have no glyph");
    }

    [Test]
    public void FontMatrixSetsUnitsPerEm()
    {
        var builder = SquareFont();
        builder.FontMatrix = "[0.0005 0 0 0.0005 0 0]";

        Assert.That(Typeface.LoadType1(builder.BuildPdfFontFile()).CurrentFont.UnitsPerEm, Is.EqualTo(2000));
    }

    [Test]
    public void UnencryptedCharStringsReadWithLenIVMinusOne()
    {
        var builder = SquareFont();
        builder.LenIV = -1;

        var typeface = Typeface.LoadType1(builder.BuildPdfFontFile());
        typeface.GetGlyphByName("A", out var a);

        Assert.That(a.BoundingRectangle.Width, Is.EqualTo(500));
    }

    [Test]
    public void FlexDrawsTwoCurvesThroughItsSixPoints()
    {
        var builder = new Type1FontBuilder().WithStandardSubrs();
        var code = CharString(0, 1000, "hsbw", 1, "callsubr",
            100, 20, "rmoveto", 2, "callsubr",
            -70, 0, "rmoveto", 2, "callsubr",
            40, 0, "rmoveto", 2, "callsubr",
            30, 0, "rmoveto", 2, "callsubr",
            30, 0, "rmoveto", 2, "callsubr",
            40, 0, "rmoveto", 2, "callsubr",
            30, -20, "rmoveto", 2, "callsubr",
            50, 200, 0, 0, "callsubr",
            0, -100, "rlineto", "closepath", "endchar");

        var commands = Run(builder, code);

        Assert.That(Describe(commands), Is.EqualTo(new[]
        {
            "rmoveto 0 0",
            "rrcurveto 30 20 40 0 30 0",
            "rrcurveto 30 0 40 0 30 -20",
            "rlineto 0 -100",
        }));
    }

    [Test]
    public void SubroutinesDivisionAndHintReplacementAreFollowed()
    {
        var builder = new Type1FontBuilder().WithStandardSubrs();
        builder.Subrs.Add(CharString(0, 1000, 2, "div", "rlineto", "return"));
        builder.Subrs.Add(CharString(1, "hstem", "return"));
        var code = CharString(10, 600, "hsbw", 6, 4, "callsubr", 5, "callsubr", 300, "hlineto", "closepath",
            "endchar");

        var commands = Run(builder, code);

        Assert.That(Describe(commands), Is.EqualTo(new[] { "rmoveto 10 0", "rlineto 0 500", "rlineto 300 0" }));
    }

    [Test]
    public void CurveShorthandsAndSbwPlacePoints()
    {
        var builder = new Type1FontBuilder();
        var code = CharString(20, 30, 700, 0, "sbw", 10, 20, 30, 40, "vhcurveto", 1, 2, 3, 4, "hvcurveto",
            "endchar");

        var commands = Run(builder, code);

        Assert.That(Describe(commands), Is.EqualTo(new[]
        {
            "rmoveto 20 30", "rrcurveto 0 10 20 30 40 0", "rrcurveto 1 0 2 3 0 4",
        }));
    }

    [Test]
    public void AccentedGlyphPlacesTheAccentRelativeToTheBase()
    {
        var builder = SquareFont();
        builder.Glyphs.Add(("Aacute", CharString(50, 600, "hsbw", 100, 150, 600, 65, 194, "seac")));

        var typeface = Typeface.LoadType1(builder.BuildPdfFontFile());
        typeface.GetGlyphByName("Aacute", out var aacute);

        Assert.That(aacute.AdvanceWidth, Is.EqualTo(600));
        Assert.That(aacute.Outlines.Count, Is.EqualTo(2));
        // The accent's side bearing point lands at the base's (50) plus adx (150), ady (600) up.
        var accent = aacute.Outlines.Last().Points.Select(p => (p.X, p.Y)).Distinct().ToArray();
        Assert.That(accent, Is.EqualTo(new[] { (200.0, 600.0), (200.0, 700.0), (300.0, 700.0), (300.0, 600.0) }));
    }

    [Test]
    public void SubroutineCallingItselfStopsInsteadOfHanging()
    {
        var builder = SquareFont();
        builder.Subrs.Add(CharString(5, "callsubr", "return"));
        builder.Glyphs.Add(("loop", CharString(0, 500, "hsbw", 5, "callsubr", "endchar")));

        var typeface = Typeface.LoadType1(builder.BuildPdfFontFile());
        typeface.GetGlyphByName("loop", out var loop);

        Assert.That(loop.IsInvalid, Is.True);
        Assert.That(loop.HasOutlines, Is.False);
    }

    [Test]
    public void MalformedCharStringsLeaveOnlyTheirGlyphInvalid()
    {
        var builder = SquareFont();
        builder.Glyphs.Add(("short", CharString(0, 500, "hsbw", 10, "rlineto", "endchar")));
        builder.Glyphs.Add(("missing", CharString(0, 500, "hsbw", 99, "callsubr", "endchar")));
        builder.Glyphs.Add(("cut", [139, 139, 13, 248]));
        builder.Glyphs.Add(("deep", CharString(Enumerable.Repeat((object)1, 60).ToArray())));

        var typeface = Typeface.LoadType1(builder.BuildPdfFontFile());

        foreach (var name in new[] { "short", "missing", "cut", "deep" })
        {
            typeface.GetGlyphByName(name, out var glyph);
            Assert.That(glyph.IsInvalid, Is.True, name);
        }

        typeface.GetGlyphByName("A", out var a);
        Assert.That(a.IsInvalid, Is.False);
    }

    [Test]
    public void SubroutinesThatEachCallTheNextTwiceDoNotSlowLoading()
    {
        var builder = SquareFont();
        var first = builder.Subrs.Count;
        for (var i = 0; i < 30; i++)
        {
            builder.Subrs.Add(CharString(first + i + 1, "callsubr", first + i + 1, "callsubr", "return"));
        }

        builder.Subrs.Add(CharString("return"));
        for (var g = 0; g < 2000; g++)
        {
            builder.Glyphs.Add(("g" + g, CharString(first, "callsubr", 0, 500, "hsbw", "endchar")));
        }

        var watch = Stopwatch.StartNew();
        var typeface = Typeface.LoadType1(builder.BuildPdfFontFile());

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
        foreach (var glyph in typeface.Glyphs)
        {
            _ = glyph.BoundingRectangle;
        }

        typeface.GetGlyphByName("g0", out var g0);
        Assert.That(g0.IsInvalid, Is.True, "drawing it runs past the step limit");
        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5), "all glyphs share the font's budget");
    }

    [Test]
    public void EveryByteChangedGivesAFontOrInvalidData()
    {
        var data = SquareFont().BuildPdfFontFile();
        foreach (var value in new byte[] { 0, 32, 255 })
        {
            for (var at = 0; at < data.Length; at += 2)
            {
                var changed = (byte[])data.Clone();
                changed[at] = value;
                try
                {
                    foreach (var glyph in Typeface.LoadType1(changed).Glyphs)
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

    [Test]
    public void DataThatIsNotType1IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Typeface.LoadType1("%!PS just a program\n"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => Typeface.LoadType1([]));
        Assert.Throws<InvalidDataException>(() => Typeface.LoadType1([0x80, 1, 0xff, 0xff, 0xff, 0x7f]));
    }

    [Test]
    public void TruncatedFontIsRejectedOrReadsWhatItHolds()
    {
        var data = SquareFont().BuildPdfFontFile();
        for (var length = 0; length < data.Length; length += 7)
        {
            var cut = data[..length];
            try
            {
                var typeface = Typeface.LoadType1(cut);
                foreach (var glyph in typeface.Glyphs)
                {
                    _ = glyph.BoundingRectangle;
                }
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    private static List<Command> Run(Type1FontBuilder builder, byte[] code)
    {
        var subrs = new Dictionary<int, byte[]>();
        for (var i = 0; i < builder.Subrs.Count; i++)
        {
            subrs[i] = builder.Subrs[i];
        }

        var interpreter = new Type1CharStringInterpreter(subrs, _ => null, FontWorkBudget.ForCharStrings(0, 0));
        interpreter.Run(code);
        return interpreter.Commands;
    }

    private static string[] Describe(List<Command> commands) => commands
        .Select(c => c.Operator + " " + string.Join(" ", c.Operands.Select(o => o.Value.ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture))))
        .ToArray();
}
