using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Png;

/// <summary>Writes pictures as PNG (PNG, third edition), several frames as APNG. The smallest color type that holds
/// every pixel is chosen - a palette for up to 256 colors, gray when every pixel is, no alpha when every pixel is
/// opaque - each row filtered by the filter whose bytes sum least, as PNG suggests; a <see cref="PngImage"/>'s
/// metadata and frame timing are written with it.</summary>
public static class PngEncoder
{
    private const int ChunkData = 1 << 20;

    /// <summary>Writes <paramref name="image"/> to <paramref name="output"/>: gray, RGB, BGR, RGBA and BGRA pixels of 8
    /// or 16 bits. Two-channel pixels throw <see cref="NotSupportedException"/> - they say nothing of being gray and
    /// alpha; metadata PNG cannot hold - a keyword not of 1 to 79 printable Latin-1 characters, text with a NUL -
    /// <see cref="ArgumentException"/>.</summary>
    public static void Encode(IRawBitmap image, Stream output, CompressionLevel level = CompressionLevel.Optimal)
    {
        var description = image.GetImageDescription();
        var (channels, wide, bgr) = Layout(description.Format);
        int width = (int)description.Width, height = (int)description.Height;
        // Frames are asked for one at a time, twice - an animation's are composed when asked for, and all of them
        // together could be any size.
        var frameCount = (int)Math.Max(1, image.FramesCount);
        var source = new PngSourceRows(image, width, height, channels, wide, bgr);
        var png = image as PngImage;
        var (colorType, depth, palette) = Choose(source, frameCount, png?.Metadata?.Background);
        var colorMetadata = new MemoryStream();
        WriteColorMetadata(new ChunkWriter(colorMetadata), png?.Metadata);
        var otherMetadata = new MemoryStream();
        WriteOtherMetadata(new ChunkWriter(otherMetadata), png?.Metadata, colorType, depth, palette);
        var writer = new ChunkWriter(output);
        writer.Signature();
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        (header[8], header[9]) = ((byte)depth, (byte)colorType);
        writer.Chunk("IHDR", header);
        colorMetadata.WriteTo(output);
        if (palette != null)
        {
            var colors = palette.Colors;
            var entries = new byte[colors.Count * 3];
            var alphas = new List<byte>();
            for (var i = 0; i < colors.Count; i++)
            {
                (entries[i * 3], entries[i * 3 + 1], entries[i * 3 + 2]) =
                    ((byte)colors[i], (byte)(colors[i] >> 8), (byte)(colors[i] >> 16));
                alphas.Add((byte)(colors[i] >> 24));
            }

            writer.Chunk("PLTE", entries);
            while (alphas.Count > 0 && alphas[^1] == 255)
            {
                alphas.RemoveAt(alphas.Count - 1);
            }

            if (alphas.Count > 0)
            {
                writer.Chunk("tRNS", alphas.ToArray());
            }
        }

        otherMetadata.WriteTo(output);
        var animated = frameCount > 1;
        var sequence = 0u;
        if (animated)
        {
            var control = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(control, (uint)frameCount);
            BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4), image.NumberOfReplays);
            writer.Chunk("acTL", control);
        }

        for (var f = 0; f < frameCount; f++)
        {
            if (animated)
            {
                var (numerator, denominator) = ImageBitmap.TimingOf(image) switch
                {
                    PngImage timed when f < timed.Frames.Count =>
                        (timed.Frames[f].DelayNumerator, timed.Frames[f].DelayDenominator),
                    Gif.GifImage timed when f < timed.Frames.Count =>
                        (timed.Frames[f].DelayCentiseconds, (ushort)100),
                    _ => ((ushort)1, (ushort)10)
                };
                var control = new byte[26];
                BinaryPrimitives.WriteUInt32BigEndian(control, sequence++);
                BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(4), (uint)width);
                BinaryPrimitives.WriteUInt32BigEndian(control.AsSpan(8), (uint)height);
                BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(20), numerator);
                BinaryPrimitives.WriteUInt16BigEndian(control.AsSpan(22), denominator);
                writer.Chunk("fcTL", control);
            }

            var compressed = Compress(Scanlines(source, f, colorType, depth, palette), level);
            for (var at = 0; at < compressed.Length; at += ChunkData)
            {
                var part = compressed.AsSpan(at, Math.Min(ChunkData, compressed.Length - at));
                if (f == 0)
                {
                    writer.Chunk("IDAT", part);
                }
                else
                {
                    var data = new byte[part.Length + 4];
                    BinaryPrimitives.WriteUInt32BigEndian(data, sequence++);
                    part.CopyTo(data.AsSpan(4));
                    writer.Chunk("fdAT", data);
                }
            }
        }

        writer.Chunk("IEND", []);
        output.Flush();
    }

    private static (int Channels, bool Wide, bool Bgr) Layout(SurfaceFormat format) => (Format)format switch
    {
        Format.R8_UNORM or Format.R8_UINT or Format.R8_SRGB => (1, false, false),
        Format.R8G8B8_UNORM or Format.R8G8B8_SRGB => (3, false, false),
        Format.B8G8R8_UNORM or Format.B8G8R8_SRGB => (3, false, true),
        Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB => (4, false, false),
        Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB => (4, false, true),
        Format.R16_UNORM or Format.R16_UINT => (1, true, false),
        Format.R16G16B16_UNORM => (3, true, false),
        Format.R16G16B16A16_UNORM => (4, true, false),
        _ => throw new NotSupportedException($"PNG is not written from {(Format)format} pixels.")
    };

    // The color type and depth that hold every pixel of every frame and the background, and the palette when one does.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static (int ColorType, int Depth, PngPalette Palette) Choose(PngSourceRows source, int frameCount,
        (ushort R, ushort G, ushort B)? background)
    {
        var gray = background is not { } shade || shade.R == shade.G && shade.G == shade.B;
        var opaque = true;
        var palette = source.Wide ? null : new PngPalette();
        var narrow = source.Wide ? null : new uint[source.Width];
        var wide = source.Wide ? new ulong[source.Width] : null;
        for (var f = 0; f < frameCount && (gray || opaque || palette != null); f++)
        {
            var pixels = source.Frame(f);
            for (var y = 0; y < source.Height && (gray || opaque || palette != null); y++)
            {
                if (wide != null)
                {
                    source.ReadRow(pixels, y, wide);
                    foreach (var pixel in wide)
                    {
                        gray &= (ushort)pixel == (ushort)(pixel >> 16) && (ushort)pixel == (ushort)(pixel >> 32);
                        opaque &= pixel >> 48 == ushort.MaxValue;
                    }

                    continue;
                }

                var row = source.ReadRow(pixels, y, narrow);
                gray &= IsGray(row);
                opaque &= IsOpaque(row);
                if (palette == null)
                {
                    continue;
                }

                var last = ~row[0];
                foreach (var pixel in row)
                {
                    if (pixel != last)
                    {
                        last = pixel;
                        if (!palette.Add(pixel))
                        {
                            palette = null;
                            break;
                        }
                    }
                }
            }
        }

        var depth = source.Wide ? 16 : 8;
        if (gray)
        {
            return (opaque ? 0 : 4, depth, null);
        }

        // bKGD of a palette image is an entry of it: the background takes one when no pixel's color is it.
        if (palette != null && background is { } color && BackgroundIndex(palette, color) < 0
            && !palette.Add((uint)(color.R >> 8) | (uint)(color.G >> 8 << 8) | (uint)(color.B >> 8 << 16) | 0xFF000000))
        {
            palette = null;
        }

        if (palette == null)
        {
            return (opaque ? 2 : 6, depth, null);
        }

        palette.Order();
        var count = palette.Colors.Count;
        return (3, count <= 2 ? 1 : count <= 4 ? 2 : count <= 16 ? 4 : 8, palette);
    }

    // The frame's rows in the chosen form, each after its filter byte.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static byte[] Scanlines(PngSourceRows source, int frame, int colorType, int depth, PngPalette palette)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var rowBytes = (source.Width * channels * depth + 7) / 8;
        var bytesPerPixel = Math.Max(1, channels * depth / 8);
        var result = new byte[(long)(rowBytes + 1) * source.Height];
        var raw = new byte[rowBytes];
        var prior = new byte[rowBytes];
        var best = new byte[rowBytes];
        var narrow = source.Wide ? null : new uint[source.Width];
        var wide = source.Wide ? new ulong[source.Width] : null;
        var pixels = source.Frame(frame);
        for (var y = 0; y < source.Height; y++)
        {
            if (wide != null)
            {
                source.ReadRow(pixels, y, wide);
                WriteRow(wide, channels, raw);
            }
            else
            {
                var pixelRow = source.ReadRow(pixels, y, narrow);
                if (palette != null)
                {
                    WriteIndices(pixelRow, palette, depth, raw);
                }
                else
                {
                    WriteRow(pixelRow, channels, raw);
                }
            }

            var row = result.AsSpan((int)((long)y * (rowBytes + 1)), rowBytes + 1);
            var filter = palette != null || depth < 8 ? (byte)0 : Filter(raw, prior, best, bytesPerPixel);
            row[0] = filter;
            (filter == 0 ? raw : best).AsSpan().CopyTo(row[1..]);
            (raw, prior) = (prior, raw);
        }

        return result;
    }

    private static bool IsGray(ReadOnlySpan<uint> row)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var low = Vector128.Create(0xFFFFu);
            for (; x + 4 <= row.Length; x += 4)
            {
                var pixels = Vector128.Create(row.Slice(x, 4));
                if (((pixels ^ (pixels >>> 8)) & low) != Vector128<uint>.Zero)
                {
                    return false;
                }
            }
        }

        for (; x < row.Length; x++)
        {
            if (((row[x] ^ (row[x] >> 8)) & 0xFFFF) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOpaque(ReadOnlySpan<uint> row)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var opaque = Vector128.Create(0xFF000000u);
            for (; x + 4 <= row.Length; x += 4)
            {
                if ((Vector128.Create(row.Slice(x, 4)) & opaque) != opaque)
                {
                    return false;
                }
            }
        }

        for (; x < row.Length; x++)
        {
            if (row[x] < 0xFF000000)
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteIndices(ReadOnlySpan<uint> row, PngPalette palette, int depth, byte[] raw)
    {
        var last = ~row[0];
        var index = 0;
        if (depth < 8)
        {
            Array.Clear(raw);
        }

        for (var x = 0; x < row.Length; x++)
        {
            if (row[x] != last)
            {
                last = row[x];
                index = palette.IndexOf(last);
            }

            if (depth == 8)
            {
                raw[x] = (byte)index;
            }
            else
            {
                raw[x * depth / 8] |= (byte)(index << (8 - depth - x * depth % 8));
            }
        }
    }

    private static void WriteRow(ReadOnlySpan<uint> row, int channels, byte[] raw)
    {
        if (channels == 4)
        {
            MemoryMarshal.AsBytes(row).CopyTo(raw);
            return;
        }

        for (var x = 0; x < row.Length; x++)
        {
            var pixel = row[x];
            switch (channels)
            {
                case 1:
                    raw[x] = (byte)pixel;
                    break;
                case 2:
                    (raw[2 * x], raw[2 * x + 1]) = ((byte)pixel, (byte)(pixel >> 24));
                    break;
                default:
                    (raw[3 * x], raw[3 * x + 1], raw[3 * x + 2]) = ((byte)pixel, (byte)(pixel >> 8),
                        (byte)(pixel >> 16));
                    break;
            }
        }
    }

    private static void WriteRow(ulong[] row, int channels, byte[] raw)
    {
        var samples = MemoryMarshal.Cast<byte, ushort>(raw.AsSpan());
        for (var x = 0; x < row.Length; x++)
        {
            var pixel = row[x];
            var at = x * channels;
            switch (channels)
            {
                case 1:
                    samples[at] = BigEndian(pixel);
                    break;
                case 2:
                    (samples[at], samples[at + 1]) = (BigEndian(pixel), BigEndian(pixel >> 48));
                    break;
                default:
                    (samples[at], samples[at + 1], samples[at + 2]) =
                        (BigEndian(pixel), BigEndian(pixel >> 16), BigEndian(pixel >> 32));
                    if (channels == 4)
                    {
                        samples[at + 3] = BigEndian(pixel >> 48);
                    }

                    break;
            }
        }
    }

    private static ushort BigEndian(ulong sample) => BitConverter.IsLittleEndian
        ? BinaryPrimitives.ReverseEndianness((ushort)sample)
        : (ushort)sample;

    private static int BackgroundIndex(PngPalette palette, (ushort R, ushort G, ushort B) color)
    {
        var rgb = (uint)(color.R >> 8) | (uint)(color.G >> 8 << 8) | (uint)(color.B >> 8 << 16);
        for (var i = 0; i < palette.Colors.Count; i++)
        {
            if ((palette.Colors[i] & 0xFFFFFF) == rgb)
            {
                return i;
            }
        }

        return -1;
    }

    // 12.8: the filter of the five whose output, its bytes taken as signed, sums least in magnitude - the sums of all
    // five in one pass - and its output into best.
    private static byte Filter(byte[] raw, byte[] prior, byte[] best, int bytesPerPixel)
    {
        long none = 0, sub = 0, up = 0, average = 0, paeth = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            int x = raw[i], above = prior[i];
            int left = 0, upperLeft = 0;
            if (i >= bytesPerPixel)
            {
                (left, upperLeft) = (raw[i - bytesPerPixel], prior[i - bytesPerPixel]);
            }

            none += Math.Abs((int)(sbyte)x);
            sub += Math.Abs((int)(sbyte)(x - left));
            up += Math.Abs((int)(sbyte)(x - above));
            average += Math.Abs((int)(sbyte)(x - ((left + above) >> 1)));
            paeth += Math.Abs((int)(sbyte)(x - PngRaster.Paeth((byte)left, (byte)above, (byte)upperLeft)));
        }

        Span<long> sums = [none, sub, up, average, paeth];
        byte chosen = 0;
        for (byte type = 1; type < 5; type++)
        {
            if (sums[type] < sums[chosen])
            {
                chosen = type;
            }
        }

        for (var i = 0; i < raw.Length; i++)
        {
            int left = 0, upperLeft = 0;
            if (i >= bytesPerPixel)
            {
                (left, upperLeft) = (raw[i - bytesPerPixel], prior[i - bytesPerPixel]);
            }

            best[i] = (byte)(raw[i] - chosen switch
            {
                0 => 0,
                1 => left,
                2 => prior[i],
                3 => (left + prior[i]) >> 1,
                _ => PngRaster.Paeth((byte)left, prior[i], (byte)upperLeft)
            });
        }

        return chosen;
    }

    private static byte[] Compress(byte[] scanlines, CompressionLevel level)
    {
        using var memory = new MemoryStream(scanlines.Length / 2 + 64);
        using (var deflater = new ZLibStream(memory, level))
        {
            deflater.Write(scanlines);
        }

        return memory.ToArray();
    }

    private static void WriteColorMetadata(ChunkWriter writer, PngMetadata metadata)
    {
        if (metadata == null)
        {
            return;
        }

        if (metadata.Gamma is { } gamma)
        {
            var data = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)Math.Round(gamma * 100000));
            writer.Chunk("gAMA", data);
        }

        if (metadata.Chromaticities is { } c)
        {
            var data = new byte[32];
            double[] points = [c.WhiteX, c.WhiteY, c.RedX, c.RedY, c.GreenX, c.GreenY, c.BlueX, c.BlueY];
            for (var i = 0; i < 8; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4), (uint)Math.Round(points[i] * 100000));
            }

            writer.Chunk("cHRM", data);
        }

        // PNG has iCCP and sRGB not both: the profile, which ranks above sRGB, is kept.
        if (metadata.IccProfile != null)
        {
            var name = Keyword(string.IsNullOrEmpty(metadata.IccProfileName) ? "ICC profile" : metadata.IccProfileName);
            writer.Chunk("iCCP", [.. name, 0, 0, .. Deflate(metadata.IccProfile)]);
        }
        else if (metadata.SrgbIntent is { } intent)
        {
            writer.Chunk("sRGB", [(byte)intent]);
        }
    }

    private static void WriteOtherMetadata(ChunkWriter writer, PngMetadata metadata, int colorType, int depth,
        PngPalette palette)
    {
        if (metadata == null)
        {
            return;
        }

        if (metadata.Background is { } background)
        {
            var data = BackgroundOf(background, colorType, depth, palette);
            if (data != null)
            {
                writer.Chunk("bKGD", data);
            }
        }

        if (metadata.PixelsPerUnit is { } units)
        {
            var data = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(data, units.X);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), units.Y);
            data[8] = units.Meters ? (byte)1 : (byte)0;
            writer.Chunk("pHYs", data);
        }

        if (metadata.Time is { } time)
        {
            // A time of unspecified kind is taken as UTC, as tIME's are read.
            var utc = time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time;
            var data = new byte[7];
            BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)utc.Year);
            (data[2], data[3], data[4], data[5], data[6]) = ((byte)utc.Month, (byte)utc.Day, (byte)utc.Hour,
                (byte)utc.Minute, (byte)utc.Second);
            writer.Chunk("tIME", data);
        }

        foreach (var text in metadata.Texts)
        {
            WriteText(writer, text);
        }

        if (metadata.Exif != null)
        {
            writer.Chunk("eXIf", metadata.Exif);
        }
    }

    // bKGD in the image's own samples - of its depth, or its palette's entry; null for a color the palette lacks.
    private static byte[] BackgroundOf((ushort R, ushort G, ushort B) color, int colorType, int depth,
        PngPalette palette)
    {
        int Sample(ushort value) => depth == 16 ? value : value >> 8;
        switch (colorType)
        {
            case 3:
                var index = BackgroundIndex(palette, color);
                return index < 0 ? null : [(byte)index];
            case 0 or 4:
                var gray = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(gray, (ushort)Sample(color.R));
                return gray;
            default:
                var rgb = new byte[6];
                BinaryPrimitives.WriteUInt16BigEndian(rgb, (ushort)Sample(color.R));
                BinaryPrimitives.WriteUInt16BigEndian(rgb.AsSpan(2), (ushort)Sample(color.G));
                BinaryPrimitives.WriteUInt16BigEndian(rgb.AsSpan(4), (ushort)Sample(color.B));
                return rgb;
        }
    }

    // 11.3.3.2: 1 to 79 Latin-1 characters that print, spaces only single and inside.
    private static byte[] Keyword(string keyword)
    {
        var bytes = Latin1(keyword ?? "", "A PNG keyword");
        if (bytes.Length is 0 or > 79 || bytes[0] == ' ' || bytes[^1] == ' '
            || bytes.AsSpan().IndexOf("  "u8) >= 0 || Array.Exists(bytes, b => b is < 32 or > 126 and < 161))
        {
            throw new ArgumentException($"\"{keyword}\" is not a PNG keyword: 1 to 79 printable Latin-1 characters.");
        }

        return bytes;
    }

    private static byte[] Latin1(string text, string what)
    {
        if (text.Contains('\0') || text.Any(c => c > 255))
        {
            throw new ArgumentException($"{what} holds a NUL or a character Latin-1 lacks: \"{text}\".");
        }

        return Encoding.Latin1.GetBytes(text);
    }

    private static byte[] Utf8(string text, string what) => text.Contains('\0')
        ? throw new ArgumentException($"{what} holds a NUL.")
        : Encoding.UTF8.GetBytes(text);

    private static void WriteText(ChunkWriter writer, PngText text)
    {
        var keyword = Keyword(text.Keyword);
        if (text.International)
        {
            var value = Utf8(text.Text ?? "", "A PNG text");
            var language = text.Language ?? "";
            if (language.Any(c => c is not ('-' or '_' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')))
            {
                throw new ArgumentException($"\"{language}\" is not a language tag.");
            }

            writer.Chunk("iTXt", [.. keyword, 0, text.Compressed ? (byte)1 : (byte)0, 0,
                .. Encoding.ASCII.GetBytes(language), 0,
                .. Utf8(text.TranslatedKeyword ?? "", "A PNG translated keyword"), 0,
                .. text.Compressed ? Deflate(value) : value]);
            return;
        }

        var latin = Latin1(text.Text ?? "", "A PNG text");
        if (text.Compressed)
        {
            writer.Chunk("zTXt", [.. keyword, 0, 0, .. Deflate(latin)]);
        }
        else
        {
            writer.Chunk("tEXt", [.. keyword, 0, .. latin]);
        }
    }

    private static byte[] Deflate(byte[] bytes) => Compress(bytes, CompressionLevel.Optimal);

    private sealed class ChunkWriter(Stream output)
    {
        public void Signature() => output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        public void Chunk(string type, ReadOnlySpan<byte> data)
        {
            Span<byte> head = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(head, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, head[4..]);
            output.Write(head);
            output.Write(data);
            var crc = PngCrc.Finish(PngCrc.Update(PngCrc.Update(PngCrc.Start, head[4..]), data));
            Span<byte> tail = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
            output.Write(tail);
        }
    }
}
