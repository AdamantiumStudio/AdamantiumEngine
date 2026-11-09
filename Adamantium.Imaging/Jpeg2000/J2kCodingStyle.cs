using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The COD marker (ITU-T T.800, A.6.1): the progression, layers, component transform, packet markers and
/// the coding style of every component that has no COC of its own.</summary>
internal sealed class J2kCodingStyle
{
    public J2kProgression Progression { get; private init; }

    public int Layers { get; private init; }

    public bool ComponentTransform { get; private init; }

    public bool StartOfPacketMarkers { get; private init; }

    public bool EndOfHeaderMarkers { get; private init; }

    public J2kComponentStyle Component { get; private init; }

    public static J2kCodingStyle Read(BigEndianReader reader)
    {
        var flags = reader.ReadByte();
        var progression = reader.ReadByte();
        var layers = reader.ReadUInt16();
        var transform = reader.ReadByte();
        if (progression > 4 || layers == 0)
        {
            throw new InvalidDataException("A JPEG 2000 coding style has no progression or layers.");
        }

        return new J2kCodingStyle
        {
            Progression = (J2kProgression)progression,
            Layers = layers,
            ComponentTransform = transform != 0,
            StartOfPacketMarkers = (flags & 2) != 0,
            EndOfHeaderMarkers = (flags & 4) != 0,
            Component = J2kComponentStyle.Read(reader, (flags & 1) != 0),
        };
    }
}
