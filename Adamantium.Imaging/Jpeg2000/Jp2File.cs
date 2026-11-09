using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The boxes of a JP2 file (ITU-T T.800, annex I) that say how to show its codestream: the color
/// specification, a palette with its component mapping, and the channel definitions.</summary>
internal sealed class Jp2File
{
    private const uint Signature = 0x6A502020;
    private const uint Header = 0x6A703268;
    private const uint ColorSpecification = 0x636F6C72;
    private const uint PaletteBox = 0x70636C72;
    private const uint ComponentMapping = 0x636D6170;
    private const uint ChannelDefinition = 0x63646566;
    private const uint Codestream = 0x6A703263;

    public Jpeg2000ColorSpace ColorSpace { get; private set; }

    public byte[] IccProfile { get; private set; }

    /// <summary>The palette's columns, each its entries, or null.</summary>
    public int[][] Palette { get; private set; }

    public int[] PaletteBits { get; private set; }

    /// <summary>For each channel of a palette image: its component and the palette column, or -1 for direct use.
    /// </summary>
    public (int Component, int Column)[] Mapping { get; private set; }

    /// <summary>For each channel: its type (0 color, 1 opacity, 2 premultiplied opacity) and association.</summary>
    public Dictionary<int, (int Type, int Association)> Channels { get; } = [];

    public int CodestreamStart { get; private set; }

    public int CodestreamEnd { get; private set; }

    /// <summary>Reads the boxes of a JP2 file, or takes the data as a bare codestream when it starts with SOC.
    /// </summary>
    public static Jp2File Read(byte[] data)
    {
        var file = new Jp2File();
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0x4F)
        {
            file.CodestreamEnd = data.Length;
            return file;
        }

        file.ReadBoxes(new BigEndianReader(data, 0, data.Length));
        if (file.CodestreamEnd == 0)
        {
            throw new InvalidDataException("A JPEG 2000 file has no codestream.");
        }

        return file;
    }

    private void ReadBoxes(BigEndianReader reader)
    {
        var first = true;
        while (reader.Remaining >= 8 && CodestreamEnd == 0)
        {
            var (type, content) = Box(reader);
            if (first && type != Signature)
            {
                throw new InvalidDataException("The data is not a JPEG 2000 file.");
            }

            first = false;
            if (type == Codestream)
            {
                CodestreamStart = content.Position;
                CodestreamEnd = content.End;
            }
            else if (type == Header)
            {
                while (content.Remaining >= 8)
                {
                    var (inner, box) = Box(content);
                    switch (inner)
                    {
                        case ColorSpecification:
                            ReadColor(box);
                            break;
                        case PaletteBox:
                            ReadPalette(box);
                            break;
                        case ComponentMapping:
                            ReadMapping(box);
                            break;
                        case ChannelDefinition:
                            ReadChannels(box);
                            break;
                    }
                }
            }
        }
    }

    private static (uint Type, BigEndianReader Content) Box(BigEndianReader reader)
    {
        var start = reader.Position;
        long length = reader.ReadUInt32();
        var type = reader.ReadUInt32();
        if (length == 1)
        {
            length = (long)reader.ReadUInt64();
        }
        else if (length == 0)
        {
            length = reader.End - start;
        }

        var contentStart = reader.Position;
        if (length < contentStart - start || length > reader.End - start)
        {
            throw new InvalidDataException("A JPEG 2000 box is longer than its file.");
        }

        var end = (int)(start + length);
        reader.Position = end;
        return (type, new BigEndianReader(reader.Data, contentStart, end));
    }

    private void ReadColor(BigEndianReader content)
    {
        if (ColorSpace != Jpeg2000ColorSpace.Unknown)
        {
            return;
        }

        var method = content.ReadByte();
        content.Skip(2);
        if (method == 1)
        {
            ColorSpace = content.ReadUInt32() switch
            {
                12 => Jpeg2000ColorSpace.Cmyk,
                16 => Jpeg2000ColorSpace.Srgb,
                17 => Jpeg2000ColorSpace.Gray,
                18 => Jpeg2000ColorSpace.Sycc,
                _ => Jpeg2000ColorSpace.Unknown,
            };
        }
        else if (method is 2 or 3)
        {
            ColorSpace = Jpeg2000ColorSpace.Icc;
            IccProfile = content.Data[content.Position..content.End];
        }
    }

    private void ReadPalette(BigEndianReader content)
    {
        var entries = content.ReadUInt16();
        var columns = content.ReadByte();
        if (entries is 0 or > 1024 || columns == 0)
        {
            throw new InvalidDataException("A JPEG 2000 palette has no entries.");
        }

        PaletteBits = new int[columns];
        for (var i = 0; i < columns; i++)
        {
            PaletteBits[i] = (content.ReadByte() & 0x7F) + 1;
            if (PaletteBits[i] > 32)
            {
                throw new InvalidDataException("A JPEG 2000 palette has entries too deep.");
            }
        }

        Palette = new int[columns][];
        for (var i = 0; i < columns; i++)
        {
            Palette[i] = new int[entries];
        }

        for (var e = 0; e < entries; e++)
        {
            for (var i = 0; i < columns; i++)
            {
                var bytes = (PaletteBits[i] + 7) / 8;
                long value = 0;
                for (var b = 0; b < bytes; b++)
                {
                    value = (value << 8) | content.ReadByte();
                }

                Palette[i][e] = (int)value;
            }
        }
    }

    private void ReadMapping(BigEndianReader content)
    {
        var mapping = new List<(int, int)>();
        while (content.Remaining >= 4)
        {
            var component = content.ReadUInt16();
            var type = content.ReadByte();
            var column = content.ReadByte();
            mapping.Add((component, type == 1 ? column : -1));
        }

        Mapping = mapping.ToArray();
    }

    private void ReadChannels(BigEndianReader content)
    {
        var count = content.ReadUInt16();
        for (var i = 0; i < count; i++)
        {
            var channel = content.ReadUInt16();
            var type = content.ReadUInt16();
            var association = content.ReadUInt16();
            Channels[channel] = (type, association);
        }
    }
}
