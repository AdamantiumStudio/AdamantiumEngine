using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>The region segment information field (ITU-T T.88, 7.4.1): a region's size, place and combination.
/// </summary>
internal sealed class Jbig2RegionInfo
{
    /// <summary>The bytes of the field.</summary>
    public const int Length = 17;

    public uint Width { get; private init; }

    public uint Height { get; private init; }

    public int X { get; private init; }

    public int Y { get; private init; }

    public Jbig2Combination Combination { get; private init; }

    public static Jbig2RegionInfo Read(BigEndianReader bytes)
    {
        var info = new Jbig2RegionInfo
        {
            Width = bytes.ReadUInt32(),
            Height = bytes.ReadUInt32(),
            X = bytes.ReadInt32(),
            Y = bytes.ReadInt32(),
            Combination = (Jbig2Combination)(bytes.ReadByte() & 7),
        };
        if (info.Combination > Jbig2Combination.Replace)
        {
            throw new InvalidDataException("A JBIG2 region has no combination operator " + info.Combination + ".");
        }

        return info;
    }
}
