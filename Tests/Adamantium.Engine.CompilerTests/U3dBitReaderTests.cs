using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models.U3d;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>The bit decoder of ECMA-363 clause 10 against an encoder written from the same clause: plain values, static
/// and dynamic contexts over many values, the no compression profile, and data that ends too soon.</summary>
[TestFixture]
public class U3dBitReaderTests
{
    private static readonly uint[] StaticRanges = [1, 2, 3, 7, 255, 256, 1000, 0x3FFD, 0x3FFE, 0x3FFF, 100000];

    [Test]
    public void PlainValues_AreTheirLittleEndianBytes()
    {
        var writer = new U3dTestBitWriter();
        writer.WriteU8(0xA5);
        writer.WriteU16(0x1234);
        writer.WriteU32(0xDEADBEEF);
        writer.WriteF32(-1.5f);
        writer.WriteF64(Math.PI);
        writer.WriteString("Ünï");
        var expected = new List<byte> { 0xA5 };
        expected.AddRange(BitConverter.GetBytes((ushort)0x1234));
        expected.AddRange(BitConverter.GetBytes(0xDEADBEEF));
        expected.AddRange(BitConverter.GetBytes(-1.5f));
        expected.AddRange(BitConverter.GetBytes(Math.PI));
        expected.AddRange(BitConverter.GetBytes((ushort)5));
        expected.AddRange("Ünï"u8.ToArray());
        var bytes = writer.ToArray();

        Assert.That(bytes, Is.EqualTo(expected.ToArray()), "the coder leaves a block of plain values byte for byte");
        var reader = new U3dBitReader(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadU8(), Is.EqualTo(0xA5));
            Assert.That(reader.ReadU16(), Is.EqualTo(0x1234));
            Assert.That(reader.ReadU32(), Is.EqualTo(0xDEADBEEF));
            Assert.That(reader.ReadF32(), Is.EqualTo(-1.5f));
            Assert.That(reader.ReadF64(), Is.EqualTo(Math.PI));
            Assert.That(reader.ReadString(), Is.EqualTo("Ünï"));
            Assert.That(reader.PlainBytesRead, Is.EqualTo(bytes.Length));
            Assert.That(reader.IsPastEnd, Is.False);
        });
    }

    [Test]
    public void StaticContexts_RoundTrip([Values(1, 2, 3, 4, 5)] int seed)
    {
        var random = new Random(seed);
        var contexts = new List<U3dContext>();
        var values = new List<uint>();
        var sizes = new List<int>();
        var writer = new U3dTestBitWriter();
        for (var index = 0; index < 4000; index++)
        {
            var range = StaticRanges[random.Next(StaticRanges.Length)];
            var size = range <= 256 && random.Next(3) == 0 ? 1 : range <= 0xFFFF && random.Next(2) == 0 ? 2 : 4;
            var value = (uint)random.NextInt64(range);
            var context = U3dContext.Static(range);
            if (size == 1)
            {
                writer.WriteCompressedU8(context, (byte)value);
            }
            else if (size == 2)
            {
                writer.WriteCompressedU16(context, (ushort)value);
            }
            else
            {
                writer.WriteCompressedU32(context, value);
            }

            contexts.Add(context);
            values.Add(value);
            sizes.Add(size);
            if (random.Next(10) == 0)
            {
                writer.WriteU32((uint)index);
                contexts.Add(default);
                values.Add((uint)index);
                sizes.Add(0);
            }
        }

        AssertReadsBack(writer.ToArray(), contexts, values, sizes);
    }

    [Test]
    public void DynamicContexts_RoundTrip([Values(11, 12, 13, 14, 15)] int seed)
    {
        var random = new Random(seed);
        var names = new[] { "cShading", "cPosDiffX", "cDiffuseColorSign", "cFaceCnt" };
        var contexts = new List<U3dContext>();
        var values = new List<uint>();
        var sizes = new List<int>();
        var writer = new U3dTestBitWriter();
        for (var index = 0; index < 30000; index++)
        {
            var name = names[random.Next(names.Length)];
            var context = U3dContext.Dynamic(name);
            var size = name == "cDiffuseColorSign" ? 1 : name == "cFaceCnt" ? 2 : 4;
            uint value = random.Next(20) switch
            {
                0 => (uint)random.Next(0x10000, int.MaxValue),
                1 => 0xFFFE,
                < 15 => (uint)Math.Min(random.Next(8) * random.Next(4), 30),
                _ => (uint)random.Next(size == 1 ? 256 : 5000)
            };
            value = size == 1 ? value & 0xFF : size == 2 ? value & 0xFFFF : value;
            if (size == 1)
            {
                writer.WriteCompressedU8(context, (byte)value);
            }
            else if (size == 2)
            {
                writer.WriteCompressedU16(context, (ushort)value);
            }
            else
            {
                writer.WriteCompressedU32(context, value);
            }

            contexts.Add(context);
            values.Add(value);
            sizes.Add(size);
            if (random.Next(50) == 0)
            {
                writer.WriteF32(index);
                contexts.Add(default);
                values.Add(BitConverter.SingleToUInt32Bits(index));
                sizes.Add(0);
            }
        }

        AssertReadsBack(writer.ToArray(), contexts, values, sizes);
    }

    [Test]
    public void DynamicContext_OfOneValueOverAndOver_HalvesItsCountsAndKeepsReading()
    {
        var writer = new U3dTestBitWriter();
        var context = U3dContext.Dynamic("cStayMove0");
        for (var index = 0; index < 50000; index++)
        {
            writer.WriteCompressedU8(context, (byte)(index % 997 == 0 ? 1 : 0));
        }

        var bytes = writer.ToArray();
        Assert.That(bytes.Length, Is.LessThan(2000), "a context of one value all but always codes it in a sliver of a bit");
        var reader = new U3dBitReader(bytes);
        for (var index = 0; index < 50000; index++)
        {
            Assert.That(reader.ReadCompressedU8(context), Is.EqualTo(index % 997 == 0 ? 1 : 0), $"value {index}");
        }

        Assert.That(reader.IsPastEnd, Is.False);
    }

    [Test]
    public void NoCompressionProfile_ReadsCompressedFieldsAsPlainValues()
    {
        var writer = new U3dTestBitWriter(true);
        writer.WriteCompressedU32(U3dContext.Dynamic("cShading"), 7);
        writer.WriteCompressedU16(U3dContext.Dynamic("cDiffuseCount"), 513);
        writer.WriteCompressedU8(U3dContext.Static(10), 9);
        writer.WriteCompressedU32(U3dContext.Static(100), 99);
        var bytes = writer.ToArray();

        Assert.That(bytes, Is.EqualTo(new byte[] { 7, 0, 0, 0, 1, 2, 9, 99, 0, 0, 0 }));
        var reader = new U3dBitReader(bytes, true);
        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadCompressedU32(U3dContext.Dynamic("cShading")), Is.EqualTo(7));
            Assert.That(reader.ReadCompressedU16(U3dContext.Dynamic("cDiffuseCount")), Is.EqualTo(513));
            Assert.That(reader.ReadCompressedU8(U3dContext.Static(10)), Is.EqualTo(9));
            Assert.That(reader.ReadCompressedU32(U3dContext.Static(100)), Is.EqualTo(99));
        });
    }

    [Test]
    public void ReadingPastTheData_Throws()
    {
        var writer = new U3dTestBitWriter();
        writer.WriteCompressedU32(U3dContext.Dynamic("cShading"), 3);
        var reader = new U3dBitReader(writer.ToArray());
        reader.ReadCompressedU32(U3dContext.Dynamic("cShading"));
        Assert.That(() =>
        {
            for (var index = 0; index < 100; index++)
            {
                reader.ReadU32();
            }
        }, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CountBeyondTheData_Throws()
    {
        var reader = new U3dBitReader(new byte[16]);
        Assert.That(() => reader.CheckCount(0x7FFFFFFF, 1, "faces"),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("faces"));
        Assert.That(() => reader.CheckCount(16, 8, "bytes"), Throws.Nothing);
    }

    [Test]
    public void ValueFromAnEmptyStaticRange_Throws()
    {
        var reader = new U3dBitReader(new byte[16]);
        Assert.That(() => reader.ReadCompressedU32(U3dContext.Static(0)), Throws.TypeOf<InvalidDataException>());
    }

    private static void AssertReadsBack(byte[] bytes, List<U3dContext> contexts, List<uint> values, List<int> sizes)
    {
        var reader = new U3dBitReader(bytes);
        for (var index = 0; index < values.Count; index++)
        {
            var read = sizes[index] switch
            {
                0 => reader.ReadU32(),
                1 => reader.ReadCompressedU8(contexts[index]),
                2 => reader.ReadCompressedU16(contexts[index]),
                _ => reader.ReadCompressedU32(contexts[index])
            };
            if (read != values[index])
            {
                Assert.Fail($"value {index} in {contexts[index]} read as {read}, written as {values[index]}");
            }
        }

        Assert.That(reader.IsPastEnd, Is.False, "the values end inside the data");
        Assert.That(reader.RemainingBits, Is.LessThan(64), "the data ends where the values and the flush do");
    }
}
