using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Icc;

/// <summary>An ICC profile (ICC.1:2022, versions 2 and 4): its header and the tags a device-to-PCS transform reads -
/// the A2B tables, the colorant matrix and tone curves, the gray tone curve and the media white point.</summary>
public sealed class IccProfile
{
    private const int HeaderLength = 128;
    private const uint Signature = 0x61637370;
    private const int MaxTags = 1024;

    private readonly byte[] _data;
    private readonly Dictionary<uint, (int Offset, int Length)> _tags = [];

    private IccProfile(byte[] data)
    {
        _data = data;
    }

    /// <summary>The profile's version, major and minor.</summary>
    public Version Version { get; private init; }

    /// <summary>The profile class: input, display, output, link, color space, abstract or named color.</summary>
    public string Class { get; private init; }

    /// <summary>The color space of the device side.</summary>
    public IccColorSpace ColorSpace { get; private init; }

    /// <summary>The device side's channels.</summary>
    public int Components { get; private init; }

    /// <summary>The profile connection space: XYZ or Lab.</summary>
    public IccColorSpace ConnectionSpace { get; private init; }

    /// <summary>The intent the profile was made for.</summary>
    public IccRenderingIntent RenderingIntent { get; private init; }

    /// <summary>The media white point, XYZ relative to D50, or D50 itself when the profile does not say.</summary>
    public (double X, double Y, double Z) MediaWhite { get; private set; } = IccTransform.D50;

    /// <summary>Reads a profile; an error of the data for bytes that are not one.</summary>
    /// <exception cref="InvalidDataException">The data is not an ICC profile.</exception>
    public static IccProfile Read(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        var reader = new BigEndianReader(bytes, 0, bytes.Length);
        if (bytes.Length < HeaderLength + 4)
        {
            throw new InvalidDataException("An ICC profile is shorter than its header.");
        }

        reader.Position = 8;
        var major = reader.ReadByte();
        var minor = reader.ReadByte() >> 4;
        reader.Position = 12;
        var profileClass = Tag(reader.ReadUInt32());
        var (space, components) = Space(reader.ReadUInt32());
        var (connection, _) = Space(reader.ReadUInt32());
        reader.Position = 36;
        if (reader.ReadUInt32() != Signature)
        {
            throw new InvalidDataException("The data is not an ICC profile.");
        }

        reader.Position = 64;
        var intent = reader.ReadUInt32() & 3;
        if (connection is not (IccColorSpace.Lab or IccColorSpace.Xyz) || components == 0)
        {
            throw new InvalidDataException("An ICC profile has no connection space or device space it can have.");
        }

        var profile = new IccProfile(bytes)
        {
            Version = new Version(major, minor),
            Class = profileClass,
            ColorSpace = space,
            Components = components,
            ConnectionSpace = connection,
            RenderingIntent = (IccRenderingIntent)intent,
        };
        reader.Position = HeaderLength;
        var count = reader.ReadUInt32();
        if (count > MaxTags)
        {
            throw new InvalidDataException("An ICC profile has more tags than any needs.");
        }

        for (var i = 0; i < count; i++)
        {
            var signature = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            if (offset > bytes.Length || length > bytes.Length - offset || length < 8)
            {
                continue;
            }

            profile._tags.TryAdd(signature, ((int)offset, (int)length));
        }

        if (profile.TryTag(IccTags.MediaWhitePoint, out var white))
        {
            profile.MediaWhite = ReadXyz(white);
        }

        return profile;
    }

    internal bool Has(uint signature) => _tags.ContainsKey(signature);

    /// <summary>A reader over the tag's bytes, its type signature first.</summary>
    internal bool TryTag(uint signature, out BigEndianReader reader)
    {
        if (_tags.TryGetValue(signature, out var tag))
        {
            reader = new BigEndianReader(_data, tag.Offset, tag.Offset + tag.Length);
            return true;
        }

        reader = null;
        return false;
    }

    /// <summary>An XYZType value (ICC.1, 10.31): its first triple.</summary>
    internal static (double X, double Y, double Z) ReadXyz(BigEndianReader reader)
    {
        if (reader.ReadUInt32() != 0x58595A20)
        {
            throw new InvalidDataException("An ICC tag that should be XYZ is not.");
        }

        reader.Skip(4);
        return (reader.ReadInt32() / 65536.0, reader.ReadInt32() / 65536.0, reader.ReadInt32() / 65536.0);
    }

    private static string Tag(uint value) => new([(char)(value >> 24), (char)(value >> 16 & 0xFF),
        (char)(value >> 8 & 0xFF), (char)(value & 0xFF)]);

    private static (IccColorSpace Space, int Components) Space(uint signature) => Tag(signature) switch
    {
        "XYZ " => (IccColorSpace.Xyz, 3),
        "Lab " => (IccColorSpace.Lab, 3),
        "RGB " => (IccColorSpace.Rgb, 3),
        "GRAY" => (IccColorSpace.Gray, 1),
        "CMYK" => (IccColorSpace.Cmyk, 4),
        "CMY " => (IccColorSpace.Cmy, 3),
        "YCbr" => (IccColorSpace.YCbCr, 3),
        "HSV " => (IccColorSpace.Hsv, 3),
        "HLS " => (IccColorSpace.Hls, 3),
        "Luv " or "Yxy " => (IccColorSpace.Other, 3),
        [var n, 'C', 'L', 'R'] when Colorants.IndexOf(n) >= 0 => (IccColorSpace.Colorants, Colorants.IndexOf(n) + 2),
        _ => (IccColorSpace.Other, 0),
    };

    private const string Colorants = "23456789ABCDEF";
}
