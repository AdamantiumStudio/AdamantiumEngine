using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Adamantium.Imaging.Png;

/// <summary>A PNG file's structure (PNG, third edition; APNG): the header, palette and transparency, the metadata, and
/// where each frame's compressed data lies - read without decompressing a pixel. Chunks are checked by their CRC: a
/// broken critical one refuses the file, a broken ancillary one is passed over. An animation that breaks APNG's rules
/// reads as the still picture its file also is.</summary>
internal sealed class PngFile
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int BitDepth { get; private set; }

    /// <summary>0 gray, 2 RGB, 3 palette, 4 gray and alpha, 6 RGBA.</summary>
    public int ColorType { get; private set; }

    public bool Interlaced { get; private set; }

    /// <summary>The palette as RGBA, four bytes an entry, alphas from tRNS; 256 entries, those past the file's black.
    /// </summary>
    public byte[] Palette { get; private set; }

    /// <summary>tRNS of gray or RGB: the sample values that are transparent; null without one.</summary>
    public ushort[] TransparentKey { get; private set; }

    public PngMetadata Metadata { get; } = new();

    /// <summary>acTL's number of plays, 0 for forever.</summary>
    public uint Plays { get; private set; }

    /// <summary>The frames shown: the animation's, or the still picture alone.</summary>
    public List<PngFrameData> Frames { get; } = [];

    /// <summary>Samples of each pixel.</summary>
    public int Channels => ColorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };

    /// <summary>Whether the pixels come out of 16 bits a channel.</summary>
    public bool Wide => BitDepth == 16;

    public static PngFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("The data is not PNG: no PNG signature.");
        }

        var file = new PngFile();
        file.ReadChunks(data);
        return file;
    }

    private void ReadChunks(ReadOnlySpan<byte> data)
    {
        var still = new PngFrameData();
        var animation = new List<PngFrameData>();
        PngFrameData current = null;
        var animated = false;
        var broken = false;
        var expectedFrames = 0u;
        var sequence = 0u;
        var sawData = false;
        var dataEnded = false;
        var sawPalette = false;
        var at = 8;
        var first = true;
        while (true)
        {
            if (at + 12 > data.Length)
            {
                if (!sawData)
                {
                    throw new InvalidDataException("The PNG file ends before its image data.");
                }

                break;
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
            var type = data.Slice(at + 4, 4);
            if (length > int.MaxValue || at + 12L + length > data.Length)
            {
                if (!sawData)
                {
                    throw new InvalidDataException($"A PNG chunk of {length} bytes runs past the file's end.");
                }

                break;
            }

            var content = data.Slice(at + 8, (int)length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(data[(at + 8 + (int)length)..]);
            var name = Encoding.ASCII.GetString(type);
            var critical = (type[0] & 0x20) == 0;
            var start = at + 8;
            at += 12 + (int)length;
            if (PngCrc.Finish(PngCrc.Update(PngCrc.Update(PngCrc.Start, type), content)) != crc)
            {
                if (critical)
                {
                    throw new InvalidDataException($"The PNG {name} chunk is broken: its CRC does not match.");
                }

                continue;
            }

            if (first != (name == "IHDR"))
            {
                throw new InvalidDataException("A PNG file does not begin with its IHDR chunk.");
            }

            first = false;
            switch (name)
            {
                case "IHDR":
                    ReadHeader(content);
                    break;
                case "PLTE":
                    ReadPalette(content);
                    sawPalette = true;
                    break;
                case "tRNS":
                    var transparency = content.ToArray();
                    Ancillary(() => ReadTransparency(transparency));
                    break;
                case "IDAT":
                    if (dataEnded)
                    {
                        throw new InvalidDataException("The PNG file's IDAT chunks are not together.");
                    }

                    sawData = true;
                    still.Segments.Add((start, (int)length));
                    break;
                case "IEND":
                    goto end;
                case "acTL" when !sawData && content.Length == 8:
                    expectedFrames = BinaryPrimitives.ReadUInt32BigEndian(content);
                    Plays = BinaryPrimitives.ReadUInt32BigEndian(content[4..]);
                    animated = expectedFrames > 0;
                    break;
                case "fcTL" when animated && !broken:
                    // A frame's data comes before the next frame's fcTL.
                    if (content.Length != 26 || BinaryPrimitives.ReadUInt32BigEndian(content) != sequence++
                                             || animation.Count > 0 && animation[^1].Segments.Count == 0)
                    {
                        broken = true;
                        break;
                    }

                    // The still picture is the animation's first frame when this fcTL comes before it.
                    var frame = sawData ? new PngFrameData() : still;
                    frame.Control = ReadControl(content[4..], ref broken);
                    animation.Add(frame);
                    current = frame;
                    break;
                case "fdAT" when animated && !broken:
                    if (content.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(content) != sequence++
                                           || current == null || current == still)
                    {
                        broken = true;
                        break;
                    }

                    current.Segments.Add((start + 4, (int)length - 4));
                    break;
                default:
                    if (critical)
                    {
                        throw new NotSupportedException($"The PNG file has a critical {name} chunk, which is not read.");
                    }

                    var ancillary = content.ToArray();
                    Ancillary(() => ReadAncillary(name, ancillary));
                    break;
            }

            if (sawData && name != "IDAT")
            {
                dataEnded = true;
            }
        }

        end:
        if (!sawData)
        {
            throw new InvalidDataException("The PNG file has no image data.");
        }

        if (ColorType == 3 && !sawPalette)
        {
            throw new InvalidDataException("A PNG palette image without its palette.");
        }

        // The still picture is the canvas's size whether shown or not: data that cannot hold it would let a few bytes
        // make an animation's canvas any size.
        PngRaster.CheckCanHold(this, still, Width, Height);

        // A still picture that is the first frame covers the canvas (APNG, fcTL).
        if (animated && !broken && animation.Count == expectedFrames && animation.TrueForAll(frame =>
                frame.Segments.Count > 0 && frame.Control.X + (long)frame.Control.Width <= Width
                                         && frame.Control.Y + (long)frame.Control.Height <= Height)
            && (animation[0] != still || still.Control is { X: 0, Y: 0 } control && control.Width == Width
                && control.Height == Height))
        {
            Frames.AddRange(animation);
            return;
        }

        Plays = 0;
        still.Control = new PngFrame(0, 0, (uint)Width, (uint)Height, 0, 0, PngDispose.None, PngBlend.Source);
        Frames.Add(still);
    }

    private static PngFrame ReadControl(ReadOnlySpan<byte> content, ref bool broken)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(content);
        var height = BinaryPrimitives.ReadUInt32BigEndian(content[4..]);
        var x = BinaryPrimitives.ReadUInt32BigEndian(content[8..]);
        var y = BinaryPrimitives.ReadUInt32BigEndian(content[12..]);
        var dispose = content[20];
        var blend = content[21];
        if (width == 0 || height == 0 || dispose > 2 || blend > 1)
        {
            broken = true;
        }

        return new PngFrame(x, y, width, height, BinaryPrimitives.ReadUInt16BigEndian(content[16..]),
            BinaryPrimitives.ReadUInt16BigEndian(content[18..]), (PngDispose)Math.Min(dispose, (byte)2),
            (PngBlend)Math.Min(blend, (byte)1));
    }

    private void ReadHeader(ReadOnlySpan<byte> content)
    {
        if (content.Length != 13)
        {
            throw new InvalidDataException("A broken PNG IHDR chunk.");
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(content);
        var height = BinaryPrimitives.ReadUInt32BigEndian(content[4..]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            throw new InvalidDataException($"A PNG image of {width}×{height}.");
        }

        (Width, Height, BitDepth, ColorType) = ((int)width, (int)height, content[8], content[9]);
        var allowed = ColorType switch
        {
            0 => BitDepth is 1 or 2 or 4 or 8 or 16,
            3 => BitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => BitDepth is 8 or 16,
            _ => false
        };
        if (!allowed || content[10] != 0 || content[11] != 0 || content[12] > 1)
        {
            throw new InvalidDataException($"A PNG image of color type {ColorType} and {BitDepth} bits, compression "
                                           + $"{content[10]}, filter {content[11]}, interlace {content[12]}.");
        }

        Interlaced = content[12] == 1;
        if ((long)Width * Height * (Wide ? 8 : 4) > Array.MaxLength)
        {
            throw new NotSupportedException($"A PNG image of {Width}×{Height} is more than one array holds.");
        }
    }

    private void ReadPalette(ReadOnlySpan<byte> content)
    {
        if (content.Length % 3 != 0 || content.Length is 0 or > 768 || Palette != null)
        {
            throw new InvalidDataException("A broken PNG palette.");
        }

        Palette = new byte[256 * 4];
        for (var i = 0; i < 256; i++)
        {
            Palette[i * 4 + 3] = 255;
        }

        _paletteCount = content.Length / 3;
        for (var i = 0; i < _paletteCount; i++)
        {
            content.Slice(i * 3, 3).CopyTo(Palette.AsSpan(i * 4));
        }
    }

    private int _paletteCount;

    private void ReadTransparency(byte[] content)
    {
        switch (ColorType)
        {
            case 3 when Palette != null && content.Length <= 256:
                for (var i = 0; i < content.Length; i++)
                {
                    Palette[i * 4 + 3] = content[i];
                }

                break;
            case 0 when content.Length == 2:
                TransparentKey = [BinaryPrimitives.ReadUInt16BigEndian(content)];
                break;
            case 2 when content.Length == 6:
                TransparentKey =
                [
                    BinaryPrimitives.ReadUInt16BigEndian(content), BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(2)),
                    BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(4))
                ];
                break;
        }
    }

    // A broken ancillary chunk is as if it were not there (PNG, 13.1) - its zlib stream broken too.
    private static void Ancillary(Action read)
    {
        try
        {
            read();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException
                                              or IndexOutOfRangeException or DecoderFallbackException)
        {
        }
    }

    private void ReadAncillary(string name, byte[] content)
    {
        var metadata = Metadata;
        switch (name)
        {
            case "bKGD":
            {
                // Values past the depth, or past the palette, are not a background.
                var top = (1 << BitDepth) - 1;
                ushort Value(int at) => BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(at));
                metadata.Background = ColorType switch
                {
                    3 when content.Length == 1 && content[0] < _paletteCount => (Scale(Palette[content[0] * 4]),
                        Scale(Palette[content[0] * 4 + 1]), Scale(Palette[content[0] * 4 + 2])),
                    0 or 4 when content.Length == 2 && Value(0) <= top => Gray(Value(0)),
                    2 or 6 when content.Length == 6 && Value(0) <= top && Value(2) <= top && Value(4) <= top =>
                        (Sample(Value(0)), Sample(Value(2)), Sample(Value(4))),
                    _ => metadata.Background
                };
                break;
            }
            case "gAMA" when content.Length == 4:
                metadata.Gamma = BinaryPrimitives.ReadUInt32BigEndian(content) / 100000.0;
                break;
            case "cHRM" when content.Length == 32:
                double Point(int i) => BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(i * 4)) / 100000.0;
                metadata.Chromaticities = new PngChromaticities(Point(0), Point(1), Point(2), Point(3), Point(4),
                    Point(5), Point(6), Point(7));
                break;
            case "sRGB" when content.Length == 1 && content[0] <= 3:
                metadata.SrgbIntent = (PngRenderingIntent)content[0];
                break;
            case "iCCP":
            {
                var end = Array.IndexOf(content, (byte)0);
                if (end is < 1 or > 79 || end + 2 > content.Length || content[end + 1] != 0
                    || Keyword(content.AsSpan(0, end)) is not { } profileName)
                {
                    break;
                }

                // Inflated first: a broken profile leaves neither name nor profile.
                var profile = Inflate(content.AsSpan(end + 2));
                (metadata.IccProfileName, metadata.IccProfile) = (profileName, profile);
                break;
            }
            case "tEXt":
            {
                var end = Array.IndexOf(content, (byte)0);
                if (end is >= 1 and <= 79 && Keyword(content.AsSpan(0, end)) is { } keyword)
                {
                    metadata.Texts.Add(new PngText(keyword, UpToNul(Latin1(content.AsSpan(end + 1)))));
                }

                break;
            }
            case "zTXt":
            {
                var end = Array.IndexOf(content, (byte)0);
                if (end is >= 1 and <= 79 && end + 2 <= content.Length && content[end + 1] == 0
                    && Keyword(content.AsSpan(0, end)) is { } keyword)
                {
                    metadata.Texts.Add(new PngText(keyword, UpToNul(Latin1(Inflate(content.AsSpan(end + 2)))),
                        Compressed: true));
                }

                break;
            }
            case "iTXt":
                ReadInternationalText(content);
                break;
            case "pHYs" when content.Length == 9 && content[8] <= 1:
                metadata.PixelsPerUnit = (BinaryPrimitives.ReadUInt32BigEndian(content),
                    BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(4)), content[8] == 1);
                break;
            case "tIME" when content.Length == 7:
                metadata.Time = new DateTime(BinaryPrimitives.ReadUInt16BigEndian(content), content[2], content[3],
                    content[4], content[5], Math.Min(content[6], (byte)59), DateTimeKind.Utc);
                break;
            case "eXIf":
                metadata.Exif = content;
                break;
        }
    }

    private void ReadInternationalText(byte[] content)
    {
        var end = Array.IndexOf(content, (byte)0);
        if (end is < 1 or > 79 || end + 3 > content.Length)
        {
            return;
        }

        var compressed = content[end + 1] == 1;
        var languageEnd = Array.IndexOf(content, (byte)0, end + 3);
        var translatedEnd = languageEnd < 0 ? -1 : Array.IndexOf(content, (byte)0, languageEnd + 1);
        if (translatedEnd < 0)
        {
            return;
        }

        if (Keyword(content.AsSpan(0, end)) is not { } keyword)
        {
            return;
        }

        var text = content.AsSpan(translatedEnd + 1);
        var utf8 = new UTF8Encoding(false, true);
        var language = Encoding.ASCII.GetString(content, end + 3, languageEnd - end - 3);
        Metadata.Texts.Add(new PngText(keyword, UpToNul(utf8.GetString(compressed ? Inflate(text) : text.ToArray())),
            string.Concat(language.Where(c => c is '-' or '_' or >= 'a' and <= 'z' or >= 'A' and <= 'Z'
                or >= '0' and <= '9')),
            utf8.GetString(content, languageEnd + 1, translatedEnd - languageEnd - 1), true, compressed));
    }

    private static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    // A keyword as PNG has it (11.3.3.2) - spaces trimmed and single, as writers that slip on them are read; null for
    // one of other characters, its text then passed over.
    private static string Keyword(ReadOnlySpan<byte> bytes)
    {
        var keyword = string.Join(' ', Latin1(bytes).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return keyword.Length is 0 or > 79 || keyword.Any(c => c is < ' ' or > '~' and < '¡') ? null : keyword;
    }

    // Text holds no NUL; what a broken writer put past one is not read.
    private static string UpToNul(string text) => text.IndexOf('\0') is var at and >= 0 ? text[..at] : text;

    // Texts and profiles are small: all of a file's together past this are broken, or a bomb.
    private const long MetadataLimit = 64 << 20;

    private long _inflated;

    private byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        try
        {
            while ((read = inflater.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (_inflated + output.Length > MetadataLimit)
                {
                    throw new InvalidDataException("A PNG file's texts and profile inflate past 64 MB.");
                }
            }
        }
        finally
        {
            // What a broken stream inflated counts too: else each bomb would cost the whole limit again.
            _inflated += output.Length;
        }

        return output.ToArray();
    }

    // A sample of the image's depth as 16 bits.
    private ushort Sample(ushort value) => BitDepth == 16 ? value : (ushort)(value * 65535 / ((1 << BitDepth) - 1));

    private (ushort, ushort, ushort) Gray(ushort value)
    {
        var sample = Sample(value);
        return (sample, sample, sample);
    }

    private static ushort Scale(byte value) => (ushort)(value * 257);
}

/// <summary>A frame of a PNG file: its fcTL, and its compressed data - IDAT, or fdAT past their sequence numbers -
/// as (start, length) in the file.</summary>
internal sealed class PngFrameData
{
    public PngFrame Control { get; set; }

    public List<(int Start, int Length)> Segments { get; } = [];
}
