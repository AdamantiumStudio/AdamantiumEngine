using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Adamantium.Imaging.Gif;

/// <summary>A GIF file's structure (GIF89a and GIF87a): the logical screen, the color tables, each image with its
/// graphic control and where its data lies, the comments and the loop count. Frames are decoded from it on demand.
/// What a file has after it breaks off is passed over, the frames before kept.</summary>
internal sealed class GifFile
{
    // A byte of LZW codes makes at most this many indices - a 12-bit code of a 4095-long string, 8/12 of it a byte:
    // a picture larger than its data could fill is a bomb.
    private const long MostIndicesPerByte = 2731;

    private GifFile()
    {
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>How many times the animation plays, 0 for forever (NETSCAPE2.0's loop count is the repeats).</summary>
    public uint Plays { get; private set; } = 1;

    public List<Image> Images { get; } = new();

    public List<string> Comments { get; } = new();

    /// <summary>An image of the file: its region as the file gives it, its colors, its graphic control and its
    /// data.</summary>
    internal sealed class Image
    {
        public int X, Y, Width, Height;
        public bool Interlaced;
        public byte[] Palette;
        public int Transparent = -1;
        public ushort Delay;
        public GifDisposal Disposal;
        public int MinCodeSize;
        public byte[] Data;
    }

    public static GifFile Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 13 || !file[..6].SequenceEqual("GIF89a"u8) && !file[..6].SequenceEqual("GIF87a"u8))
        {
            throw new InvalidDataException("Not a GIF file.");
        }

        var gif = new GifFile
        {
            Width = BinaryPrimitives.ReadUInt16LittleEndian(file[6..]),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(file[8..])
        };
        var fields = file[10];
        var at = 13;
        byte[] global = null;
        int transparent = -1;
        ushort delay = 0;
        var disposal = GifDisposal.Unspecified;
        long data = 0;
        try
        {
            if ((fields & 0x80) != 0)
            {
                global = Table(file, ref at, fields);
            }

            while (at < file.Length)
            {
                // The trailer ends the file; so does a block of no kind GIF has, as browsers end it there.
                var introducer = file[at++];
                if (introducer is not (0x21 or 0x2C))
                {
                    break;
                }

                if (introducer == 0x21)
                {
                    var label = file[at++];
                    var blocks = SubBlocks(file, ref at);
                    switch (label)
                    {
                        case 0xF9 when blocks.Count > 0 && blocks[0].Length >= 4:
                        {
                            var control = blocks[0];
                            disposal = (GifDisposal)((control[0] >> 2) & 7) is var kind and <= GifDisposal.Previous
                                ? kind
                                : GifDisposal.Unspecified;
                            delay = BinaryPrimitives.ReadUInt16LittleEndian(control.AsSpan(1));
                            transparent = (control[0] & 1) != 0 ? control[3] : -1;
                            break;
                        }
                        case 0xFF when blocks.Count >= 2 && blocks[0].Length == 11 && blocks[1].Length >= 3
                                                         && blocks[1][0] == 1
                                                         && (blocks[0].AsSpan().SequenceEqual("NETSCAPE2.0"u8)
                                                             || blocks[0].AsSpan().SequenceEqual("ANIMEXTS1.0"u8)):
                        {
                            var loops = BinaryPrimitives.ReadUInt16LittleEndian(blocks[1].AsSpan(1));
                            gif.Plays = loops == 0 ? 0u : loops + 1u;
                            break;
                        }
                        case 0xFE:
                        {
                            var text = new List<byte>();
                            foreach (var block in blocks)
                            {
                                text.AddRange(block);
                            }

                            gif.Comments.Add(Encoding.Latin1.GetString(text.ToArray()));
                            break;
                        }
                    }

                    continue;
                }

                var image = new Image
                {
                    X = BinaryPrimitives.ReadUInt16LittleEndian(file[at..]),
                    Y = BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 2)..]),
                    Width = BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 4)..]),
                    Height = BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 6)..]),
                    Transparent = transparent,
                    Delay = delay,
                    Disposal = disposal
                };
                var local = file[at + 8];
                at += 9;
                image.Interlaced = (local & 0x40) != 0;
                image.Palette = (local & 0x80) != 0 ? Table(file, ref at, local) : global;
                image.MinCodeSize = file[at++];
                if (image.MinCodeSize is < 1 or > 11)
                {
                    // Broken here: the images before are the file.
                    if (gif.Images.Count == 0)
                    {
                        throw new InvalidDataException($"A GIF image of LZW code size {image.MinCodeSize}.");
                    }

                    break;
                }

                var stream = new MemoryStream();
                foreach (var block in SubBlocks(file, ref at))
                {
                    stream.Write(block);
                }

                // An image is read a row at a time and as far as its data goes, so it may claim any size; only the
                // screen, made whole, is held to what the data could fill.
                image.Data = stream.ToArray();
                data += image.Data.Length;
                gif.Images.Add(image);
                (transparent, delay, disposal) = (-1, 0, GifDisposal.Unspecified);
            }
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // The file breaks off: the images before it are the file.
        }

        if (gif.Images.Count == 0)
        {
            throw new InvalidDataException("The GIF file has no image.");
        }

        // A screen of no size is the images' together.
        if (gif.Width == 0 || gif.Height == 0)
        {
            foreach (var image in gif.Images)
            {
                gif.Width = Math.Max(gif.Width, image.X + image.Width);
                gif.Height = Math.Max(gif.Height, image.Y + image.Height);
            }
        }

        if ((long)gif.Width * gif.Height > MostIndicesPerByte * data + (1 << 20))
        {
            throw new InvalidDataException($"GIF data of {data} bytes cannot fill a {gif.Width}×{gif.Height} screen.");
        }

        if ((long)gif.Width * gif.Height * 4 > Array.MaxLength)
        {
            throw new NotSupportedException($"A GIF screen of {gif.Width}×{gif.Height} is larger than an array holds.");
        }

        return gif;
    }

    // A color table of 2^(n+1) RGB entries, n the low three bits of its fields.
    private static byte[] Table(ReadOnlySpan<byte> file, ref int at, byte fields)
    {
        var size = 3 << ((fields & 7) + 1);
        var table = file.Slice(at, size).ToArray();
        at += size;
        return table;
    }

    // Data sub-blocks up to the empty one, or to the end of a file that breaks off in them.
    private static List<byte[]> SubBlocks(ReadOnlySpan<byte> file, ref int at)
    {
        var blocks = new List<byte[]>();
        while (at < file.Length)
        {
            int size = file[at++];
            if (size == 0)
            {
                return blocks;
            }

            size = Math.Min(size, file.Length - at);
            blocks.Add(file.Slice(at, size).ToArray());
            at += size;
        }

        return blocks;
    }
}
