using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Jpeg;

/// <summary>Encodes a picture as baseline JPEG (ITU-T T.81) in a JFIF file: gray as one component, color as YCbCr
/// with every component at full resolution. Quantization is Annex K.1's tables scaled by the quality; the Huffman
/// tables are made for the picture (Annex K.2).</summary>
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
    /// <paramref name="quality"/>, 1 to 100: gray formats as gray, the others' color, alpha dropped. Samples of 16
    /// bits keep their high 8.</summary>
    public static void Encode(IRawBitmap image, Stream output, int quality)
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
        var blocksAcross = (width + 7) / 8;
        var blocksDown = (height + 7) / 8;
        var coefficients = new short[components][];
        var tables = new[] { Scaled(LuminanceTable, quality), Scaled(ChrominanceTable, quality) };
        Span<float> block = stackalloc float[64 * 3];
        for (var c = 0; c < components; c++)
        {
            coefficients[c] = new short[blocksAcross * blocksDown * 64];
        }

        // Samples: the edge pixels repeated past the picture's edge, then the forward DCT and quantization.
        for (var by = 0; by < blocksDown; by++)
        {
            for (var bx = 0; bx < blocksAcross; bx++)
            {
                for (var y = 0; y < 8; y++)
                {
                    var row = Math.Min(by * 8 + y, height - 1);
                    for (var x = 0; x < 8; x++)
                    {
                        var column = Math.Min(bx * 8 + x, width - 1);
                        var at = ((long)row * width + column) * channels * bytes;
                        var high = bytes - 1;
                        if (gray)
                        {
                            block[y * 8 + x] = pixels[at + high] - 128;
                            continue;
                        }

                        float r = pixels[at + red * bytes + high], g = pixels[at + bytes + high],
                            b = pixels[at + (2 - red) * bytes + high];
                        block[y * 8 + x] = 0.299f * r + 0.587f * g + 0.114f * b - 128;
                        block[64 + y * 8 + x] = -0.168736f * r - 0.331264f * g + 0.5f * b;
                        block[128 + y * 8 + x] = 0.5f * r - 0.418688f * g - 0.081312f * b;
                    }
                }

                for (var c = 0; c < components; c++)
                {
                    Forward(block.Slice(c * 64, 64), tables[c == 0 ? 0 : 1],
                        coefficients[c].AsSpan((by * blocksAcross + bx) * 64, 64));
                }
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
        Headers(writer, width, height, components, tables, dc, ac);
        var bits = new JpegBitWriter(output);
        var predictors = new int[components];
        for (var b = 0; b < blocksAcross * blocksDown; b++)
        {
            for (var c = 0; c < components; c++)
            {
                var table = c == 0 ? 0 : 1;
                Emit(bits, coefficients[c].AsSpan(b * 64, 64), ref predictors[c], dc[table], ac[table]);
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

    private static void Headers(BinaryWriter writer, int width, int height, int components, ushort[][] tables,
        JpegHuffmanCode[] dc, JpegHuffmanCode[] ac)
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
            (frame[6 + 3 * c], frame[7 + 3 * c], frame[8 + 3 * c]) = ((byte)(c + 1), 0x11, (byte)(c == 0 ? 0 : 1));
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

/// <summary>A Huffman code made for symbol counts (ITU-T T.81, Annex K.2): code lengths by pairing the two rarest,
/// cut to 16 bits, then codes in order of length (C.2).</summary>
internal sealed class JpegHuffmanCode
{
    private readonly byte[] _counts = new byte[16];
    private byte[] _symbols;
    private readonly int[] _codes = new int[256];
    private readonly int[] _sizes = new int[256];

    public JpegHuffmanCode(int[] counts)
    {
        // Counts so skewed that a code passes 32 bits are halved until none does.
        var scaled = (int[])counts.Clone();
        int[] size;
        while (!Lengths(scaled, out size))
        {
            for (var i = 0; i < 256; i++)
            {
                scaled[i] = scaled[i] == 0 ? 0 : (scaled[i] + 1) / 2;
            }
        }

        var bits = new int[33];
        for (var i = 0; i < 257; i++)
        {
            if (size[i] > 0)
            {
                bits[size[i]]++;
            }
        }

        Assign(size, bits);
    }

    // K.2: the code length of each symbol, and of the one kept back; false when one passes 32 bits.
    private static bool Lengths(int[] counts, out int[] size)
    {
        var frequency = new long[257];
        for (var i = 0; i < 256; i++)
        {
            frequency[i] = counts[i];
        }

        // One code point kept back, so no code is all ones.
        frequency[256] = 1;
        size = new int[257];
        var others = new int[257];
        Array.Fill(others, -1);
        while (true)
        {
            int first = -1, second = -1;
            for (var i = 0; i < 257; i++)
            {
                if (frequency[i] > 0 && (first < 0 || frequency[i] <= frequency[first]))
                {
                    first = i;
                }
            }

            for (var i = 0; i < 257; i++)
            {
                if (frequency[i] > 0 && i != first && (second < 0 || frequency[i] <= frequency[second]))
                {
                    second = i;
                }
            }

            if (second < 0)
            {
                break;
            }

            frequency[first] += frequency[second];
            frequency[second] = 0;
            for (size[first]++; others[first] >= 0; size[first]++)
            {
                first = others[first];
            }

            others[first] = second;
            for (size[second]++; others[second] >= 0; size[second]++)
            {
                second = others[second];
            }
        }

        return Array.TrueForAll(size, length => length <= 32);
    }

    private void Assign(int[] size, int[] bits)
    {
        // K.3: lengths past 16 bits folded in, keeping a prefix code.
        for (var i = 32; i > 16; i--)
        {
            while (bits[i] > 0)
            {
                var j = i - 2;
                while (bits[j] == 0)
                {
                    j--;
                }

                bits[i] -= 2;
                bits[i - 1]++;
                bits[j + 1] += 2;
                bits[j]--;
            }
        }

        var longest = 16;
        while (bits[longest] == 0)
        {
            longest--;
        }

        bits[longest]--;
        var symbols = new System.Collections.Generic.List<byte>();
        for (var length = 1; length <= 32; length++)
        {
            for (var symbol = 0; symbol < 256; symbol++)
            {
                if (size[symbol] == length)
                {
                    symbols.Add((byte)symbol);
                }
            }
        }

        // The symbols in order of their original lengths take the adjusted lengths in turn.
        var ordered = symbols.ToArray();
        var code = 0;
        var index = 0;
        for (var length = 1; length <= 16; length++)
        {
            _counts[length - 1] = (byte)bits[length];
            for (var i = 0; i < bits[length]; i++, index++, code++)
            {
                _codes[ordered[index]] = code;
                _sizes[ordered[index]] = length;
            }

            code <<= 1;
        }

        _symbols = ordered[..index];
    }

    /// <summary>The DHT entry for this code as table <paramref name="id"/> (class in the high nibble).</summary>
    public byte[] Table(byte id) => [id, .. _counts, .. _symbols];

    public void Write(JpegBitWriter bits, int symbol) => bits.Write(_codes[symbol], _sizes[symbol]);
}

/// <summary>Bits into a JPEG entropy-coded segment, most significant first, 0xFF stuffed with 0x00 (F.1.2.3);
/// the last byte padded with ones.</summary>
internal sealed class JpegBitWriter
{
    private readonly Stream _output;
    private readonly byte[] _buffer = new byte[1 << 16];
    private int _used;
    private ulong _bits;
    private int _count;

    public JpegBitWriter(Stream output)
    {
        _output = output;
    }

    public void Write(int value, int size)
    {
        if (size == 0)
        {
            return;
        }

        _bits = (_bits << size) | ((uint)value & ((1u << size) - 1));
        _count += size;
        while (_count >= 8)
        {
            _count -= 8;
            Put((byte)(_bits >> _count));
        }
    }

    public void Flush()
    {
        if (_count > 0)
        {
            Write((1 << (8 - _count)) - 1, 8 - _count);
        }

        _output.Write(_buffer, 0, _used);
        _used = 0;
    }

    private void Put(byte value)
    {
        if (_used + 2 > _buffer.Length)
        {
            _output.Write(_buffer, 0, _used);
            _used = 0;
        }

        _buffer[_used++] = value;
        if (value == 0xFF)
        {
            _buffer[_used++] = 0;
        }
    }
}
