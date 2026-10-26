using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Dds;

/// <summary>A DDS file (Microsoft, "Programming Guide for DDS"): 1D, 2D, cube and volume textures, arrays of them,
/// every mip level, in any format the DX10 header names that Vulkan has, and the older header's compressed, masked and
/// paletted formats. The texels are kept as the file has them, layer by layer and level by level; formats whose alpha the
/// file leaves out read as opaque, and older formats few devices sample - 24-bit, 4-4-4-4, 3-3-2, luminance, alpha
/// alone, paletted - read widened to 8-bit RGBA (16-bit luminance to 16-bit RGBA). Direct3D's limits hold: at most 2048
/// layers and 2048 slices.</summary>
public sealed class DdsImage : IRawBitmap, ISubresources
{
    private const uint Magic = 0x20534444;
    private const int PaletteBytes = 256 * 4;

    private readonly byte[] _data;
    private readonly long[] _starts;
    private readonly int[] _levelStart;

    private DdsImage(ImageDescription description, byte[] data, long[] starts, int[] levelStart)
    {
        (Description, _data, _starts, _levelStart) = (description, data, starts, levelStart);
    }

    /// <summary>What the file holds.</summary>
    public ImageDescription Description { get; }

    public uint Width => Description.Width;

    public uint Height => Description.Height;

    public SurfaceFormat PixelFormat => Description.Format;

    /// <summary>False: a DDS's layers are parts of one texture, not frames to play.</summary>
    public bool IsMultiFrame => false;

    public bool HasMipLevels => Description.MipLevels > 1;

    public uint MipLevelsCount => Description.MipLevels;

    public uint NumberOfReplays => 0;

    public uint FramesCount => 1;

    /// <summary>The bytes of the first level of the first layer.</summary>
    public ulong TotalSizeInBytes => (ulong)(_starts[1] - _starts[0]);

    /// <summary>Reads the DDS in <paramref name="file"/>.</summary>
    public static DdsImage Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 128 || BinaryPrimitives.ReadUInt32LittleEndian(file) != Magic
                              || BinaryPrimitives.ReadUInt32LittleEndian(file[4..]) != 124)
        {
            throw new InvalidDataException("Not a DDS file.");
        }

        uint height = Field(file, 8), width = Field(file, 12), depth = Field(file, 20);
        uint levels = Math.Max(1, Field(file, 24)), pixelFlags = Field(file, 76), fourCC = Field(file, 80);
        var caps2 = Field(file, 108);
        var dimension = (caps2 & 0x200) != 0 ? TextureDimension.TextureCube
            : (caps2 & 0x200000) != 0 ? TextureDimension.Texture3D
            : TextureDimension.Texture2D;
        var layers = dimension == TextureDimension.TextureCube ? 6u : 1u;
        var at = 128;
        Format format;
        bool opaque;
        var widening = DdsWidening.None;
        (int First, int Second)? swapped;
        if ((pixelFlags & DdsFormats.FourCCFlag) != 0 && fourCC == DdsFormats.Dx10)
        {
            if (file.Length < 148)
            {
                throw new InvalidDataException("A DDS file ends in its DX10 header.");
            }

            var dxgi = BinaryPrimitives.ReadUInt32LittleEndian(file[128..]);
            var resource = BinaryPrimitives.ReadUInt32LittleEndian(file[132..]);
            var cube = (BinaryPrimitives.ReadUInt32LittleEndian(file[136..]) & 0x4) != 0;
            var arraySize = Math.Max(1, BinaryPrimitives.ReadUInt32LittleEndian(file[140..]));
            format = DdsFormats.OfDxgi(dxgi);
            opaque = DdsFormats.IgnoresAlpha(dxgi);
            swapped = DdsFormats.Swapped(dxgi);
            if (cube && arraySize > uint.MaxValue / 6)
            {
                throw new InvalidDataException($"A DDS of {arraySize} cubes.");
            }

            (dimension, layers) = resource switch
            {
                2 => (TextureDimension.Texture1D, arraySize),
                3 when cube => (TextureDimension.TextureCube, arraySize * 6),
                3 => (TextureDimension.Texture2D, arraySize),
                4 => (TextureDimension.Texture3D, 1u),
                _ => throw new InvalidDataException($"A DDS resource of dimension {resource}.")
            };
            if (dimension == TextureDimension.Texture1D)
            {
                if (height > 1)
                {
                    throw new InvalidDataException($"A 1D DDS of {height} rows.");
                }

                height = 1;
            }
            if (format == Format.UNDEFINED)
            {
                throw new NotSupportedException($"DDS of DXGI format {dxgi} is not read.");
            }

            at = 148;
        }
        else
        {
            opaque = false;
            if ((pixelFlags & DdsFormats.FourCCFlag) != 0)
            {
                format = DdsFormats.OfFourCC(fourCC);
                swapped = DdsFormats.Swapped(DdsFormats.DxgiOfFourCC(fourCC));
            }
            else
            {
                swapped = null;
                uint bits = Field(file, 84), red = Field(file, 88), green = Field(file, 92), blue = Field(file, 96),
                    alpha = Field(file, 100);
                (format, opaque) = DdsFormats.OfMasks(pixelFlags, bits, red, green, blue, alpha);
                (format, widening) = format == Format.UNDEFINED
                    ? DdsFormats.OfPacked(pixelFlags, bits, red, green, blue, alpha) switch
                    {
                        DdsPacked.Rgb332 => (Format.R8_UNORM, DdsWidening.Rgb332),
                        DdsPacked.Argb8332 => (Format.R8G8_UNORM, DdsWidening.Argb8332),
                        DdsPacked.LuminanceAlpha44 => (Format.R8_UNORM, DdsWidening.LuminanceAlpha44),
                        DdsPacked.Palette => (Format.R8_UNORM, DdsWidening.Palette),
                        DdsPacked.PaletteAlpha => (Format.R8G8_UNORM, DdsWidening.PaletteAlpha),
                        _ => (Format.UNDEFINED, DdsWidening.None)
                    }
                    : (format, WideningOf(format, pixelFlags));
            }

            if (format == Format.UNDEFINED)
            {
                throw new NotSupportedException("A DDS of a format the older header has that is not read.");
            }

            if (widening is DdsWidening.Palette or DdsWidening.PaletteAlpha)
            {
                at += PaletteBytes;
                if (file.Length < at)
                {
                    throw new InvalidDataException("A DDS file ends in its palette.");
                }
            }
        }

        if (width == 0 || height == 0)
        {
            throw new InvalidDataException($"A DDS of {width}×{height}.");
        }

        if (dimension == TextureDimension.TextureCube && width != height)
        {
            throw new InvalidDataException($"A DDS cube of {width}×{height} faces.");
        }

        if (dimension != TextureDimension.Texture3D)
        {
            depth = 1;
        }

        depth = Math.Max(1, depth);
        if (layers > DdsFormats.MostLayers || depth > DdsFormats.MostLayers)
        {
            throw new InvalidDataException(
                $"A DDS of {layers} layers and {depth} slices: Direct3D's textures have at most {DdsFormats.MostLayers} of each.");
        }

        levels = Math.Min(levels, Image.CalculateMipLevels(width, height, depth, MipMapCount.Auto));
        var description = new ImageDescription
        {
            Dimension = dimension, Width = width, Height = height, Depth = depth,
            ArraySize = dimension == TextureDimension.Texture3D ? 1 : layers, MipLevels = levels, Format = format
        };

        var available = file.Length - at;
        long layerBytes = 0;
        try
        {
            for (var level = 0; level < levels; level++)
            {
                layerBytes = checked(layerBytes + SliceBytes(description, level) * Math.Max(1, depth >> level));
            }

            if (checked(layerBytes * layers) > available)
            {
                throw new InvalidDataException(
                    $"A DDS file ends before its {layers} layers of {layerBytes} bytes of texels do.");
            }
        }
        catch (OverflowException)
        {
            throw new InvalidDataException($"A DDS of {width}×{height}×{depth} and {layers} layers.");
        }

        var levelStart = new int[levels + 1];
        for (var level = 0; level < levels; level++)
        {
            levelStart[level + 1] = levelStart[level] + (int)Math.Max(1, depth >> level);
        }

        var starts = new long[layers * levelStart[^1] + 1];
        long offset = 0;
        var part = 0;
        for (var layer = 0; layer < layers; layer++)
        {
            for (var level = 0; level < levels; level++)
            {
                var size = SliceBytes(description, level);
                for (var slice = 0; slice < Math.Max(1, depth >> level); slice++)
                {
                    starts[part++] = offset;
                    offset += size;
                }
            }
        }

        starts[part] = offset;
        if (at + offset > file.Length)
        {
            throw new InvalidDataException($"A DDS file ends before its {offset} bytes of texels do.");
        }

        if (offset > Array.MaxLength)
        {
            throw new NotSupportedException($"A DDS of {offset} bytes of texels is more than an array holds.");
        }

        var data = file.Slice(at, (int)offset).ToArray();
        if (swapped is var (first, second))
        {
            for (var i = 0; i + 3 < data.Length; i += 4)
            {
                (data[i + first], data[i + second]) = (data[i + second], data[i + first]);
            }
        }

        if (opaque)
        {
            Opaque(data, format);
        }

        if (widening != DdsWidening.None)
        {
            var from = TexelBlocks.Of(format).Bytes;
            var (wide, bytes) = widening == DdsWidening.Luminance16
                ? (Format.R16G16B16A16_UNORM, 8)
                : (Format.R8G8B8A8_UNORM, 4);
            if (offset / from * bytes > Array.MaxLength)
            {
                throw new NotSupportedException(
                    $"A DDS of {offset / from} texels is more than an array holds once they are widened to {wide}.");
            }

            data = Widen(data, from, widening, file.Slice(128, Math.Min(PaletteBytes, file.Length - 128)));
            for (var i = 0; i < starts.Length; i++)
            {
                starts[i] = starts[i] / from * bytes;
            }

            description.Format = wide;
        }

        return new DdsImage(description, data, starts, levelStart);
    }

    private static uint Field(ReadOnlySpan<byte> file, int at) => BinaryPrimitives.ReadUInt32LittleEndian(file[(4 + at)..]);

    private static DdsWidening WideningOf(Format format, uint flags) => format switch
    {
        Format.B8G8R8_UNORM => DdsWidening.Bgr,
        Format.R8G8B8_UNORM => DdsWidening.Rgb,
        Format.A4R4G4B4_UNORM_PACK16 => DdsWidening.Argb4444,
        Format.R8_UNORM when (flags & DdsFormats.AlphaOnly) != 0 => DdsWidening.Alpha,
        Format.R8_UNORM when (flags & DdsFormats.Luminance) != 0 => DdsWidening.Luminance,
        Format.R8G8_UNORM when (flags & DdsFormats.Luminance) != 0 => DdsWidening.LuminanceAlpha,
        Format.R16_UNORM when (flags & DdsFormats.Luminance) != 0 => DdsWidening.Luminance16,
        _ => DdsWidening.None
    };

    private static byte[] Widen(byte[] data, int bytes, DdsWidening widening, ReadOnlySpan<byte> palette)
    {
        if (widening == DdsWidening.Luminance16)
        {
            var gray = new byte[data.Length * 4];
            for (int from = 0, to = 0; from < data.Length; from += 2, to += 8)
            {
                for (var c = 0; c < 3; c++)
                {
                    (gray[to + c * 2], gray[to + c * 2 + 1]) = (data[from], data[from + 1]);
                }

                (gray[to + 6], gray[to + 7]) = (255, 255);
            }

            return gray;
        }

        var rgba = new byte[data.Length / bytes * 4];
        for (int from = 0, to = 0; from < data.Length; from += bytes, to += 4)
        {
            (rgba[to], rgba[to + 1], rgba[to + 2], rgba[to + 3]) = widening switch
            {
                DdsWidening.Bgr => (data[from + 2], data[from + 1], data[from], (byte)255),
                DdsWidening.Rgb => (data[from], data[from + 1], data[from + 2], (byte)255),
                DdsWidening.Argb4444 => (Four(data[from + 1]), High(data[from]), Four(data[from]), High(data[from + 1])),
                DdsWidening.Alpha => ((byte)0, (byte)0, (byte)0, data[from]),
                DdsWidening.Luminance => (data[from], data[from], data[from], (byte)255),
                DdsWidening.LuminanceAlpha => (data[from], data[from], data[from], data[from + 1]),
                DdsWidening.Rgb332 => (Three(data[from] >> 5), Three(data[from] >> 2), Two(data[from]), (byte)255),
                DdsWidening.Argb8332 => (Three(data[from] >> 5), Three(data[from] >> 2), Two(data[from]), data[from + 1]),
                DdsWidening.LuminanceAlpha44 => (Four(data[from]), Four(data[from]), Four(data[from]), High(data[from])),
                DdsWidening.Palette => (palette[data[from] * 4], palette[data[from] * 4 + 1], palette[data[from] * 4 + 2],
                    palette[data[from] * 4 + 3]),
                _ => (palette[data[from] * 4], palette[data[from] * 4 + 1], palette[data[from] * 4 + 2], data[from + 1])
            };
        }

        return rgba;
    }

    private static byte Four(int value) => (byte)((value & 15) * 17);

    private static byte High(int value) => (byte)((value >> 4) * 17);

    private static byte Three(int value)
    {
        var bits = value & 7;
        return (byte)(bits << 5 | bits << 2 | bits >> 1);
    }

    private static byte Two(int value) => (byte)((value & 3) * 85);

    public byte[] GetRawPixels(uint frameIndex) => Part((int)frameIndex, 0, 0).ToArray();

    public FrameData GetMipLevelData(uint mipLevel) =>
        new(Part(0, (int)mipLevel, 0).ToArray(), LevelDescription((int)mipLevel), mipLevel);

    public ImageDescription GetImageDescription() => Description;

    public FrameData GetFrameData(uint frameIndex) => new(GetRawPixels(frameIndex), LevelDescription(0));

    public void CopySubresource(int layer, int level, int slice, PixelBuffer target) =>
        Part(layer, level, slice).CopyTo(Target(target));

    private static unsafe Span<byte> Target(PixelBuffer target) => new((void*)target.DataPointer, target.BufferStride);

    private ReadOnlySpan<byte> Part(int layer, int level, int slice)
    {
        var index = layer * _levelStart[^1] + _levelStart[level] + slice;
        return _data.AsSpan((int)_starts[index], (int)(_starts[index + 1] - _starts[index]));
    }

    private ImageDescription LevelDescription(int level) => ImageDescription.Default2D(
        Math.Max(1, Width >> level), Math.Max(1, Height >> level), Description.Format);

    private static long SliceBytes(ImageDescription description, int level)
    {
        var (row, rows) = TexelBlocks.Rows(description.Format, Math.Max(1, description.Width >> level),
            Math.Max(1, description.Height >> level));
        return checked(row * rows);
    }

    private static void Opaque(byte[] data, Format format)
    {
        var (step, at, value) = format switch
        {
            Format.R8G8B8A8_UNORM or Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB => (4, 3, (byte)0xFF),
            Format.A2B10G10R10_UNORM_PACK32 or Format.A2R10G10B10_UNORM_PACK32 => (4, 3, (byte)0xC0),
            Format.A1R5G5B5_UNORM_PACK16 => (2, 1, (byte)0x80),
            Format.A4R4G4B4_UNORM_PACK16 => (2, 1, (byte)0xF0),
            _ => (0, 0, (byte)0)
        };
        for (var i = at; step > 0 && i < data.Length; i += step)
        {
            data[i] |= value;
        }
    }
}
