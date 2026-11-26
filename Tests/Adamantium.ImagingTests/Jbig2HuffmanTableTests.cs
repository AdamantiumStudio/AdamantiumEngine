using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Imaging.Jbig2;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>The Huffman tables of JBIG2: values written by T.88's own codes, B.3 assigned by hand, read back.</summary>
[TestFixture]
public class Jbig2HuffmanTableTests
{
    // Table B.1: 0 and 4 bits for 0 to 15, 10 and 8 bits from 16, 110 and 16 bits from 272, 111 and 32 bits from 65808.
    [TestCase(5, "0" + "0101")]
    [TestCase(20, "10" + "00000100")]
    [TestCase(300, "110" + "0000000000011100")]
    [TestCase(65808, "111" + "00000000000000000000000000000000")]
    public void TableB1_ReadsItsValues(int value, string bits)
    {
        Assert.That(Jbig2StandardTables.Of(1).Decode(Reader(bits)), Is.EqualTo(value));
    }

    // Every line of every standard table, at its lowest value and its highest, by codes assigned as B.3 says.
    [Test]
    public void EveryStandardTable_ReadsEveryLine()
    {
        for (var number = 1; number <= 15; number++)
        {
            var lines = LinesOf(number);
            var codes = Assigned(lines);
            var table = Jbig2StandardTables.Of(number);
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line.PrefixLength == 0)
                {
                    continue;
                }

                var prefix = Convert.ToString(codes[index], 2).PadLeft(line.PrefixLength, '0');
                var ranges = line.IsOutOfBand ? [0L] : new[] { 0L, line.RangeLength == 32 ? 12345 : (1L << line.RangeLength) - 1 };
                foreach (var offset in ranges)
                {
                    var range = line.IsOutOfBand ? "" : Convert.ToString(offset, 2).PadLeft(line.RangeLength, '0');
                    int? expected = line.IsOutOfBand ? null : (int)(line.IsLower ? line.RangeLow - offset : line.RangeLow + offset);

                    Assert.That(table.Decode(Reader(prefix + range + "1010")), Is.EqualTo(expected),
                        $"table B.{number}, line {index}, offset {offset}");
                }
            }
        }
    }

    [Test]
    public void ACodeTheTableHasNot_IsRefused_AndDataEndingInACode_Too()
    {
        // B.4 has no codeword of six ones.
        Assert.That(() => Jbig2StandardTables.Of(4).Decode(Reader("111111")), Throws.InstanceOf<System.IO.InvalidDataException>());
        Assert.That(() => Jbig2StandardTables.Of(1).Decode(Reader("10")), Throws.InstanceOf<System.IO.InvalidDataException>());
    }

    // B.3: shorter first, in their order, each code the one after, made one longer at each longer length.
    private static long[] Assigned(IReadOnlyList<Jbig2HuffmanLine> lines)
    {
        var codes = new long[lines.Count];
        long code = 0;
        var longest = lines.Max(line => line.PrefixLength);
        var previous = 0;
        for (var length = 1; length <= longest; length++)
        {
            code = (code + previous) << 1;
            previous = 0;
            for (var index = 0; index < lines.Count; index++)
            {
                if (lines[index].PrefixLength == length)
                {
                    codes[index] = code + previous++;
                }
            }
        }

        return codes;
    }

    private static IReadOnlyList<Jbig2HuffmanLine> LinesOf(int number) => Jbig2StandardTables.Of(number).Lines;

    private static Jbig2BitReader Reader(string bits)
    {
        var bytes = new byte[(bits.Length + 7) / 8];
        for (var index = 0; index < bits.Length; index++)
        {
            bytes[index / 8] |= (byte)(bits[index] == '1' ? 0x80 >> (index % 8) : 0);
        }

        return new Jbig2BitReader(bytes, 0, bytes.Length);
    }
}
