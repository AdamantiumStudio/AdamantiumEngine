using Adamantium.Pulsar.Decoding;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

[TestFixture]
public class VorbisCodebookTests
{
    // A lookup of no dimensions: vectors of nothing, which a residue would read forever, or divide by.
    [TestCase(1)]
    [TestCase(2)]
    public void ALookupOfNoDimensions_IsRefused(int lookup)
    {
        var setup = new VorbisBitWriter();
        setup.Write(0x564342, 24);
        setup.Write(0, 16);
        setup.Write(4, 24);
        setup.Write(0, 2);
        for (var entry = 0; entry < 4; entry++)
        {
            setup.Write(1, 5);
        }

        setup.Write((uint)lookup, 4);
        setup.Write(0, 32);
        setup.Write(0, 32);
        setup.Write(0, 4);
        setup.Write(0, 1);
        var bytes = setup.ToArray();

        Assert.That(() =>
        {
            var bits = new VorbisBits(bytes);
            VorbisCodebook.Read(ref bits);
        }, Throws.InstanceOf<InvalidDataException>());
    }
}
