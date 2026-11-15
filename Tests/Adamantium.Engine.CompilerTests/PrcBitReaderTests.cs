using System;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models.Prc;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>The PRC compressed types read back from the bits the specification's writing procedures give: bits from
/// the top of each byte, UnsignedInteger, Integer, String, the variable-bit-number forms, and Double through the table of
/// frequent doubles and exponents.</summary>
[TestFixture]
public class PrcBitReaderTests
{
    [Test]
    public void UnsignedIntegersAndIntegers_ReadBackAsWritten()
    {
        uint[] unsigned = [0, 1, 127, 128, 255, 256, 65535, 65536, 0x00FFFFFF, 0x01000000, uint.MaxValue];
        int[] signed = [0, 1, -1, 127, 128, -128, -129, 255, -256, 32767, -32768, int.MaxValue, int.MinValue];
        var writer = new PrcTestBitWriter();
        foreach (var value in unsigned)
        {
            writer.WriteUnsignedInteger(value);
        }

        foreach (var value in signed)
        {
            writer.WriteInteger(value);
        }

        var reader = new PrcBitReader(writer.ToArray(), "test section");

        Assert.Multiple(() =>
        {
            Assert.That(unsigned.Select(index =>reader.ReadUnsignedInteger()), Is.EqualTo(unsigned));
            Assert.That(signed.Select(index =>reader.ReadInteger()), Is.EqualTo(signed));
            Assert.That(reader.IsAtPadding, Is.True);
        });
    }

    [Test]
    public void BitsBooleansAndStrings_ReadBackAsWritten()
    {
        var writer = new PrcTestBitWriter();
        writer.WriteBits(0b101, 3);
        writer.WriteBoolean(true);
        writer.WriteCharacter(0xA5);
        writer.WriteString(null);
        writer.WriteString(string.Empty);
        writer.WriteString("Деталь Ø12");
        writer.WriteBits(5, 5);
        writer.WriteBits(19, 5);
        writer.WriteBit(true);
        writer.WriteBits(5, 3);
        writer.WriteFloatAsBytes(1.5f);

        var reader = new PrcBitReader(writer.ToArray(), "test section");

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadBits(3), Is.EqualTo(0b101));
            Assert.That(reader.ReadBoolean(), Is.True);
            Assert.That(reader.ReadCharacter(), Is.EqualTo(0xA5));
            Assert.That(reader.ReadString(), Is.Null);
            Assert.That(reader.ReadString(), Is.Empty);
            Assert.That(reader.ReadString(), Is.EqualTo("Деталь Ø12"));
            Assert.That(reader.ReadNumberOfBitsThenUnsignedInteger(), Is.EqualTo(19u), "five bits of a count, then that many");
            Assert.That(reader.ReadIntegerWithVariableBitNumber(4), Is.EqualTo(-5));
            Assert.That(reader.ReadFloatAsBytes(), Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void DoubleTable_GivesEveryExponentACodeAndNoCodeIsAPrefixOfAnother()
    {
        var codes = PrcDoubleCodes.All.ToList();
        var exponents = codes.Where(code => code.IsExponent).Select(code => code.Exponent).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(codes, Has.Count.EqualTo(2077), "the table of the specification has 2077 entries");
            Assert.That(exponents, Is.EquivalentTo(Enumerable.Range(0, 2048)));
            Assert.That(codes.Count(code => !code.IsExponent), Is.EqualTo(29));
        });

        foreach (var shorter in codes)
        {
            foreach (var longer in codes.Where(code => code.Length > shorter.Length))
            {
                Assert.That(longer.Code >> (longer.Length - shorter.Length), Is.Not.EqualTo(shorter.Code),
                    $"code {shorter.Code:X} of {shorter.Length} bits starts code {longer.Code:X} of {longer.Length}");
            }
        }
    }

    [Test]
    public void Doubles_ReadBackBitForBit()
    {
        var random = new Random(8137);
        var values = new[]
            {
                0, -0.0, 1, -1, 0.5, 0.001, -0.002, 1000, 360, 180, 12345, 999999999, Math.PI, 2 * Math.PI, 1165, 1195,
                1e-300, -1e300, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, double.PositiveInfinity,
                double.NegativeInfinity, 0.1, 1.0 / 3, 2.0 / 3, 0.2, 1.25, 4096.5, -7.75, BitConverter.UInt64BitsToDouble(0x3FF1111111111111),
                BitConverter.UInt64BitsToDouble(0x3FF1111111111112), BitConverter.UInt64BitsToDouble(0x3FF0000000000001),
                BitConverter.UInt64BitsToDouble(0x3FF0010203040506), BitConverter.UInt64BitsToDouble(0x3FF0050505050505),
                BitConverter.UInt64BitsToDouble(0x3FF0FFFFFFFFFFFF)
            }
            .Concat(Enumerable.Range(0, 5000).Select(index =>(random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-30, 30))))
            .Concat(Enumerable.Range(0, 2000).Select(index =>BitConverter.Int64BitsToDouble(random.NextInt64())))
            .Concat(Enumerable.Range(0, 2000).Select(index =>Math.Round((random.NextDouble() - 0.5) * 1000, random.Next(0, 4))))
            .Where(value => !double.IsNaN(value))
            .ToList();
        var writer = new PrcTestBitWriter();
        foreach (var value in values)
        {
            writer.WriteDouble(value);
        }

        var reader = new PrcBitReader(writer.ToArray(), "test section");

        foreach (var value in values)
        {
            var read = reader.ReadDouble();
            var expected = value == 0 ? 0ul : BitConverter.DoubleToUInt64Bits(value);
            Assert.That(BitConverter.DoubleToUInt64Bits(read), Is.EqualTo(expected), $"{value:R} reads back as {read:R}");
        }

        Assert.That(reader.IsAtPadding, Is.True);
    }

    [Test]
    public void FrequentDoubles_TakeTheirShortCode()
    {
        var writer = new PrcTestBitWriter();
        writer.WriteDouble(1);
        Assert.That(writer.BitCount, Is.EqualTo(4 + 1), "the code of 1 and its sign");

        var zero = new PrcTestBitWriter();
        zero.WriteDouble(0);
        Assert.That(zero.BitCount, Is.EqualTo(2), "zero has no sign bit");
    }

    [Test]
    public void ReadingPastTheEnd_ThrowsNamingTheSection()
    {
        var writer = new PrcTestBitWriter();
        writer.WriteUnsignedInteger(300);
        var bytes = writer.ToArray();
        var doubleWriter = new PrcTestBitWriter();
        doubleWriter.WriteDouble(Math.PI * 3);
        var cutDouble = doubleWriter.ToArray()[..3];

        Assert.Multiple(() =>
        {
            Assert.That(() => new PrcBitReader(bytes[..1], "tree of file structure 4").ReadUnsignedInteger(),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("tree of file structure 4").And.Message.Contains("bit"));
            Assert.That(() => new PrcBitReader([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], "globals").ReadUnsignedInteger(),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("past 32 bits"));
            Assert.That(() => new PrcBitReader(cutDouble, "globals").ReadDouble(),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("globals"));
        });
    }

    [Test]
    public void CountPastTheData_Throws()
    {
        var writer = new PrcTestBitWriter();
        writer.WriteUnsignedInteger(1_000_000);
        var reader = new PrcBitReader(writer.ToArray(), "tessellation of file structure 0");

        Assert.That(() => reader.ReadCount(8, "coordinates"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("1000000 coordinates"));
    }
}
