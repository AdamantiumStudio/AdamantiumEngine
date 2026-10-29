using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Jpeg;

/// <summary>Encodes a picture as baseline JPEG (ITU-T T.81) in a JFIF file: gray as one component, color as YCbCr,
/// the chroma at full resolution or shared by two or four pixels (<see cref="JpegSubsampling"/>). Quantization is Annex
/// K.1's tables scaled by the quality; the Huffman tables are made for the picture (Annex K.2).</summary>
public static class JpegEncoder
{
    // Annex K.1, natural order.
    private static readonly byte[] LuminanceTable =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62, 18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99
    ];

    private static readonly byte[] ChrominanceTable =
    [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99
    ];

    // Basis[k * 8 + n] = C(n) / 2 · cos((2k + 1) n π / 16): F = Basisᵀ·f·Basis.
    private static readonly float[] Basis = BuildBasis();

    /// <summary>Writes <paramref name="image"/>'s first frame to <paramref name="output"/> at
    /// <paramref name="quality"/>, 1 to 100, every pixel with chroma of its own: gray formats as gray, the others'
    /// color, alpha dropped. Samples of 16 bits keep their high 8.</summary>
    public static void Encode(IRawBitmap image, Stream output, int quality) =>
        Encode(image, output, quality, JpegSubsampling.None);

    /// <summary>As <see cref="Encode(IRawBitmap, Stream, int)"/>, a color picture's chroma shared as
    /// <paramref name="subsampling"/> says - each chroma sample the average of the pixels that share it. A gray picture
    /// has no chroma to share.</summary>
    public static void Encode(IRawBitmap image, Stream output, int quality, JpegSubsampling subsampling)
    {
        var description = image.GetImageDescription();
        var (channels, bytes, red) = Layout(description.Format);
        int width = (int)description.Width, height = (int)description.Height;
        if (width is 0 or > 65535 || height is 0 or > 65535)
        {
            throw new NotSupportedException($"A JPEG of {width}×{height}: each side is 1 to 65535 pixels.");
        }

        var pixels = image.GetRawPixels(0);
        var gray = channels == 1;
        var components = gray ? 1 : 3;
        var (across, down) = gray ? (1, 1) : subsampling switch
        {
            JpegSubsampling.HalfWidth => (2, 1),
            JpegSubsampling.Half => (2, 2),
            _ => (1, 1)
        };
        var lumaBlocks = across * down;
        var mcusAcross = (width + 8 * across - 1) / (8 * across);
        var mcusDown = (height + 8 * down - 1) / (8 * down);
        var mcus = mcusAcross * mcusDown;

        var coefficients = new short[components][];
        coefficients[0] = new short[mcus * lumaBlocks * 64];
        for (var c = 1; c < components; c++)
        {
            coefficients[c] = new short[mcus * 64];
        }

        ushort[][] tables = [Scaled(LuminanceTable, quality), Scaled(ChrominanceTable, quality)];
        Span<float> luma = stackalloc float[64];
        Span<float> chroma = stackalloc float[128];
        var share = 1f / lumaBlocks;
        for (var mcu = 0; mcu < mcus; mcu++)
        {
            int left = mcu % mcusAcross * 8 * across, top = mcu / mcusAcross * 8 * down;
            chroma.Clear();
            for (var block = 0; block < lumaBlocks; block++)
            {
                int blockLeft = block % across * 8, blockTop = block / across * 8;
                for (var y = 0; y < 8; y++)
                {
                    var row = Math.Min(top + blockTop + y, height - 1);
                    for (var x = 0; x < 8; x++)
                    {
                        var column = Math.Min(left + blockLeft + x, width - 1);
                        var at = ((long)row * width + column) * channels * bytes;
                        var high = bytes - 1;
                        if (gray)
                        {
                            luma[y * 8 + x] = pixels[at + high] - 128;
                            continue;
                        }

                        float r = pixels[at + red * bytes + high], g = pixels[at + bytes + high],
                            b = pixels[at + (2 - red) * bytes + high];
                        luma[y * 8 + x] = 0.299f * r + 0.587f * g + 0.114f * b - 128;
                        var sample = (blockTop + y) / down * 8 + (blockLeft + x) / across;
                        chroma[sample] += share * (-0.168736f * r - 0.331264f * g + 0.5f * b);
                        chroma[64 + sample] += share * (0.5f * r - 0.418688f * g - 0.081312f * b);
                    }
                }

                Forward(luma, tables[0], coefficients[0].AsSpan((mcu * lumaBlocks + block) * 64, 64));
            }

            for (var c = 1; c < components; c++)
            {
                Forward(chroma.Slice((c - 1) * 64, 64), tables[1], coefficients[c].AsSpan(mcu * 64, 64));
            }
        }

        var dc = new JpegHuffmanCode[2];
        var ac = new JpegHuffmanCode[2];
        for (var t = 0; t < (gray ? 1 : 2); t++)
        {
            var dcCounts = new int[257];
            var acCounts = new int[257];
            for (var c = t == 0 ? 0 : 1; c < (t == 0 ? 1 : components); c++)
            {
                Count(coefficients[c], dcCounts, acCounts);
            }

            dc[t] = new JpegHuffmanCode(dcCounts);
            ac[t] = new JpegHuffmanCode(acCounts);
        }

        var writer = new BinaryWriter(output);
        Headers(writer, width, height, components, (across << 4) | down, tables, dc, ac);
        var bits = new JpegBitWriter(output);
        var predictors = new int[components];
        for (var mcu = 0; mcu < mcus; mcu++)
        {
            for (var block = 0; block < lumaBlocks; block++)
            {
                Emit(bits, coefficients[0].AsSpan((mcu * lumaBlocks + block) * 64, 64), ref predictors[0], dc[0],
                    ac[0]);
            }

            for (var c = 1; c < components; c++)
            {
                Emit(bits, coefficients[c].AsSpan(mcu * 64, 64), ref predictors[c], dc[1], ac[1]);
            }
        }

        bits.Flush();
        output.Write([0xFF, 0xD9]);
        output.Flush();
    }

    // Samples a pixel, bytes a sample, and where red is: 0, or 2 for BGR.
    private static (int Channels, int Bytes, int Red) Layout(SurfaceFormat format) => (Format)format switch
    {
        Format.R8_UNORM or Format.R8_UINT or Format.R8_SRGB => (1, 1, 0),
        Format.R16_UNORM or Format.R16_UINT => (1, 2, 0),
        Format.R8G8B8_UNORM or Format.R8G8B8_SRGB => (3, 1, 0),
        Format.B8G8R8_UNORM or Format.B8G8R8_SRGB => (3, 1, 2),
        Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB => (4, 1, 0),
        Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB => (4, 1, 2),
        Format.R16G16B16_UNORM => (3, 2, 0),
        Format.R16G16B16A16_UNORM => (4, 2, 0),
        _ => throw new NotSupportedException($"JPEG is not written from {(Format)format} pixels.")
    };

    private static ushort[] Scaled(byte[] table, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        var scale = quality < 50 ? 5000 / quality : 200 - 2 * quality;
        var scaled = new ushort[64];
        for (var i = 0; i < 64; i++)
        {
            scaled[i] = (ushort)Math.Clamp((table[i] * scale + 50) / 100, 1, 255);
        }

        return scaled;
    }

    private static float[] BuildBasis()
    {
        var basis = new float[64];
        for (var k = 0; k < 8; k++)
        {
            for (var n = 0; n < 8; n++)
            {
                var scale = n == 0 ? 1 / Math.Sqrt(2) : 1;
                basis[k * 8 + n] = (float)(scale / 2 * Math.Cos((2 * k + 1) * n * Math.PI / 16));
            }
        }

        return basis;
    }

    // A.3.3's forward DCT, then each coefficient divided by its step and rounded half away from zero.
    private static void Forward(ReadOnlySpan<float> samples, ReadOnlySpan<ushort> table, Span<short> coefficients)
    {
        Span<float> rows = stackalloc float[64];
        for (var y = 0; y < 8; y++)
        {
            for (var u = 0; u < 8; u++)
            {
                var sum = 0f;
                for (var x = 0; x < 8; x++)
                {
                    sum += samples[y * 8 + x] * Basis[x * 8 + u];
                }

                rows[y * 8 + u] = sum;
            }
        }

        for (var v = 0; v < 8; v++)
        {
            for (var u = 0; u < 8; u++)
            {
                var sum = 0f;
                for (var y = 0; y < 8; y++)
                {
                    sum += rows[y * 8 + u] * Basis[y * 8 + v];
                }

                coefficients[v * 8 + u] = (short)MathF.Round(sum / table[v * 8 + u], MidpointRounding.AwayFromZero);
            }
        }
    }

    private static int Category(int value)
    {
        var magnitude = Math.Abs(value);
        var bits = 0;
        while (magnitude > 0)
        {
            bits++;
            magnitude >>= 1;
        }

        return bits;
    }

    private static void Count(short[] coefficients, int[] dc, int[] ac)
    {
        var predictor = 0;
        for (var b = 0; b < coefficients.Length; b += 64)
        {
            dc[Category(coefficients[b] - predictor)]++;
            predictor = coefficients[b];
            var run = 0;
            for (var k = 1; k < 64; k++)
            {
                var value = coefficients[b + JpegStream.ZigZag[k]];
                if (value == 0)
                {
                    run++;
                    continue;
                }

                for (; run > 15; run -= 16)
                {
                    ac[0xF0]++;
                }

                ac[(run << 4) | Category(value)]++;
                run = 0;
            }

            if (run > 0)
            {
                ac[0x00]++;
            }
        }
    }

    // F.1.2.1 and F.1.2.2: the DC difference, then the AC run lengths, ZRL and EOB.
    private static void Emit(JpegBitWriter bits, ReadOnlySpan<short> coefficients, ref int predictor,
        JpegHuffmanCode dc, JpegHuffmanCode ac)
    {
        var difference = coefficients[0] - predictor;
        predictor = coefficients[0];
        var size = Category(difference);
        dc.Write(bits, size);
        bits.Write(difference < 0 ? difference - 1 : difference, size);
        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            int value = coefficients[JpegStream.ZigZag[k]];
            if (value == 0)
            {
                run++;
                continue;
            }

            for (; run > 15; run -= 16)
            {
                ac.Write(bits, 0xF0);
            }

            size = Category(value);
            ac.Write(bits, (run << 4) | size);
            bits.Write(value < 0 ? value - 1 : value, size);
            run = 0;
        }

        if (run > 0)
        {
            ac.Write(bits, 0x00);
        }
    }

    private static void Headers(BinaryWriter writer, int width, int height, int components, int lumaFactors,
        ushort[][] tables, JpegHuffmanCode[] dc, JpegHuffmanCode[] ac)
    {
        writer.Write([0xFF, 0xD8]);
        Segment(writer, 0xE0, [(byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        var quantization = new byte[components == 1 ? 65 : 130];
        for (var t = 0; t < (components == 1 ? 1 : 2); t++)
        {
            quantization[t * 65] = (byte)t;
            for (var k = 0; k < 64; k++)
            {
                quantization[t * 65 + 1 + k] = (byte)tables[t][JpegStream.ZigZag[k]];
            }
        }

        Segment(writer, 0xDB, quantization);
        var frame = new byte[6 + 3 * components];
        frame[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), (ushort)width);
        frame[5] = (byte)components;
        for (var c = 0; c < components; c++)
        {
            (frame[6 + 3 * c], frame[7 + 3 * c], frame[8 + 3 * c]) =
                ((byte)(c + 1), (byte)(c == 0 ? lumaFactors : 0x11), (byte)(c == 0 ? 0 : 1));
        }

        Segment(writer, 0xC0, frame);
        for (var t = 0; t < (components == 1 ? 1 : 2); t++)
        {
            Segment(writer, 0xC4, dc[t].Table((byte)t));
            Segment(writer, 0xC4, ac[t].Table((byte)(0x10 | t)));
        }

        var scan = new byte[4 + 2 * components];
        scan[0] = (byte)components;
        for (var c = 0; c < components; c++)
        {
            (scan[1 + 2 * c], scan[2 + 2 * c]) = ((byte)(c + 1), (byte)(c == 0 ? 0x00 : 0x11));
        }

        (scan[1 + 2 * components], scan[2 + 2 * components], scan[3 + 2 * components]) = (0, 63, 0);
        Segment(writer, 0xDA, scan);
        writer.Flush();
    }

    private static void Segment(BinaryWriter writer, byte marker, ReadOnlySpan<byte> content)
    {
        Span<byte> head = [0xFF, marker, 0, 0];
        BinaryPrimitives.WriteUInt16BigEndian(head[2..], (ushort)(content.Length + 2));
        writer.Write(head);
        writer.Write(content);
    }
}
