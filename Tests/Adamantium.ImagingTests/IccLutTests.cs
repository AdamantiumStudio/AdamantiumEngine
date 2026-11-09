using System.Buffers.Binary;
using System.Collections.Generic;
using Adamantium.Imaging;
using Adamantium.Imaging.Icc;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>The lut16 matrix (ICC.1, 10.9): applied to an input in PCS XYZ, left out otherwise.</summary>
[TestFixture]
public class IccLutTests
{
    private static byte[] SwappingLut16()
    {
        var data = new List<byte>();
        void Add16(int value) => data.AddRange(new[] { (byte)(value >> 8), (byte)value });
        void Add32(int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            data.AddRange(bytes);
        }

        data.AddRange("mft2"u8.ToArray());
        Add32(0);
        data.AddRange(new byte[] { 3, 3, 2, 0 });
        foreach (var value in new[] { 0, 1, 0, 1, 0, 0, 0, 0, 1 })
        {
            Add32(value << 16);
        }

        Add16(2);
        Add16(2);
        for (var curve = 0; curve < 3; curve++)
        {
            Add16(0);
            Add16(65535);
        }

        for (var node = 0; node < 8; node++)
        {
            Add16((node >> 2 & 1) * 65535);
            Add16((node >> 1 & 1) * 65535);
            Add16((node & 1) * 65535);
        }

        for (var curve = 0; curve < 3; curve++)
        {
            Add16(0);
            Add16(65535);
        }

        return data.ToArray();
    }

    [Test]
    public void ATableOfMoreNodesThanItsTagHoldsIsRefusedBeforeItIsAllocated()
    {
        var data = SwappingLut16()[..64];
        data[10] = 176;

        var before = System.GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<System.IO.InvalidDataException>(() =>
            IccLut.Read(new BigEndianReader(data, 0, data.Length)));

        Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1 << 20));
    }

    [TestCase(true, new[] { 0.5f, 0.25f, 0.75f })]
    [TestCase(false, new[] { 0.25f, 0.5f, 0.75f })]
    public void MatrixAppliesToAnXyzInputOnly(bool xyzInput, float[] expected)
    {
        var data = SwappingLut16();
        var lut = IccLut.Read(new BigEndianReader(data, 0, data.Length), xyzInput);
        var output = new float[3];

        lut.Evaluate([0.25f, 0.5f, 0.75f], output);

        Assert.That(output, Is.EqualTo(expected).Within(1e-4));
    }
}
