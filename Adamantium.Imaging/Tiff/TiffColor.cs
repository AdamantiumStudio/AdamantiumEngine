using System;
using System.Buffers.Binary;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Tiff;

/// <summary>Turns decoded TIFF samples into a picture: colors of 8 or fewer bits into RGBA8, of up to 16 into RGBA16,
/// floats into RGBA32F, whatever the color model; signed and wider integers - data, not colors - keep their values in
/// the matching format. The Orientation tag is applied.</summary>
internal static class TiffColor
{
    private static readonly double[] D65 = [0.95047, 1.0, 1.08883];
    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];

    /// <summary>The format the picture of <paramref name="layout"/> comes in.</summary>
    public static SurfaceFormat FormatOf(TiffLayout layout)
    {
        if (layout.SampleFormat == TiffSampleFormat.Float)
        {
            return Format.R32G32B32A32_SFLOAT;
        }

        if (layout.SampleFormat == TiffSampleFormat.UnsignedInteger && layout.BitsPerSample <= 16)
        {
            return layout.BitsPerSample > 8 ? Format.R16G16B16A16_UNORM : Format.R8G8B8A8_UNORM;
        }

        var signed = layout.SampleFormat == TiffSampleFormat.SignedInteger;
        return (layout.SamplesPerPixel, TiffDecoder.ContainerBytes(layout), signed) switch
        {
            (1, 1, false) => Format.R8_UINT, (1, 1, true) => Format.R8_SINT,
            (1, 2, false) => Format.R16_UINT, (1, 2, true) => Format.R16_SINT,
            (1, 4, false) => Format.R32_UINT, (1, 4, true) => Format.R32_SINT,
            (1, 8, false) => Format.R64_UINT, (1, 8, true) => Format.R64_SINT,
            (2, 1, false) => Format.R8G8_UINT, (2, 1, true) => Format.R8G8_SINT,
            (2, 2, false) => Format.R16G16_UINT, (2, 2, true) => Format.R16G16_SINT,
            (2, 4, false) => Format.R32G32_UINT, (2, 4, true) => Format.R32G32_SINT,
            (2, 8, false) => Format.R64G64_UINT, (2, 8, true) => Format.R64G64_SINT,
            (3, 1, false) => Format.R8G8B8_UINT, (3, 1, true) => Format.R8G8B8_SINT,
            (3, 2, false) => Format.R16G16B16_UINT, (3, 2, true) => Format.R16G16B16_SINT,
            (3, 4, false) => Format.R32G32B32_UINT, (3, 4, true) => Format.R32G32B32_SINT,
            (3, 8, false) => Format.R64G64B64_UINT, (3, 8, true) => Format.R64G64B64_SINT,
            (4, 1, false) => Format.R8G8B8A8_UINT, (4, 1, true) => Format.R8G8B8A8_SINT,
            (4, 2, false) => Format.R16G16B16A16_UINT, (4, 2, true) => Format.R16G16B16A16_SINT,
            (4, 4, false) => Format.R32G32B32A32_UINT, (4, 4, true) => Format.R32G32B32A32_SINT,
            (4, 8, false) => Format.R64G64B64A64_UINT, (4, 8, true) => Format.R64G64B64A64_SINT,
            _ => throw new NotSupportedException($"TIFF data of {layout.SamplesPerPixel} samples per pixel is not read "
                                                 + "as one picture; read it by tiles instead.")
        };
    }

    /// <summary>The width and height of the picture of <paramref name="layout"/>, turned as its orientation says.
    /// </summary>
    public static (int Width, int Height) SizeOf(TiffLayout layout) =>
        layout.Orientation >= 5 ? (layout.Height, layout.Width) : (layout.Width, layout.Height);

    /// <summary>The picture of <paramref name="samples"/> laid out as <paramref name="layout"/> says.</summary>
    public static TiffBitmap ToBitmap(TiffLayout layout, TiffSamples samples)
    {
        var format = FormatOf(layout);
        var bitmap = layout.SampleFormat switch
        {
            TiffSampleFormat.Float => Floats(layout, samples),
            TiffSampleFormat.UnsignedInteger when layout.BitsPerSample <= 16 => Colors(layout, samples, format),
            _ => new TiffBitmap(format, samples.Width, samples.Height,
                layout.SamplesPerPixel * samples.ContainerBytes, samples.Data)
        };
        return Orient(bitmap, layout.Orientation);
    }

    private static TiffBitmap Colors(TiffLayout layout, TiffSamples samples, SurfaceFormat format)
    {
        var wide = layout.BitsPerSample > 8;
        var max = wide ? 65535UL : 255UL;
        var inMax = (1UL << layout.BitsPerSample) - 1;
        var pixelBytes = wide ? 8 : 4;
        var count = (long)samples.Width * samples.Height;
        var pixels = Allocate(count * pixelBytes);
        var (alpha, associated) = Alpha(layout);
        var convert = Converter(layout, samples, max, inMax);
        Span<ulong> rgb = stackalloc ulong[3];
        for (long p = 0; p < count; p++)
        {
            convert(p, rgb);
            var a = alpha < 0 ? max : Scale(Read(samples, p, alpha), inMax, max);
            if (associated)
            {
                for (var c = 0; c < 3; c++)
                {
                    rgb[c] = a == 0 ? 0 : Math.Min(max, (rgb[c] * max + a / 2) / a);
                }
            }

            for (var c = 0; c < 4; c++)
            {
                var value = c < 3 ? rgb[c] : a;
                if (wide)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan((int)(p * 8 + c * 2)), (ushort)value);
                }
                else
                {
                    pixels[p * 4 + c] = (byte)value;
                }
            }
        }

        return new TiffBitmap(format, samples.Width, samples.Height, pixelBytes, pixels);
    }

    private delegate void ColorConverter(long pixel, Span<ulong> rgb);

    private static ColorConverter Converter(TiffLayout layout, TiffSamples samples, ulong max, ulong inMax)
    {
        switch (layout.DecodedPhotometric)
        {
            case TiffPhotometric.MinIsBlack or TiffPhotometric.Mask:
                return (p, rgb) => rgb.Fill(Scale(Read(samples, p, 0), inMax, max));
            case TiffPhotometric.MinIsWhite:
                return (p, rgb) => rgb.Fill(max - Scale(Read(samples, p, 0), inMax, max));
            case TiffPhotometric.Rgb:
                return (p, rgb) =>
                {
                    for (var c = 0; c < 3; c++)
                    {
                        rgb[c] = Scale(Read(samples, p, c), inMax, max);
                    }
                };
            case TiffPhotometric.Palette:
                var map = layout.ColorMap;
                var entries = map.Length / 3;
                return (p, rgb) =>
                {
                    var index = (int)Math.Min(Read(samples, p, 0), (ulong)entries - 1);
                    for (var c = 0; c < 3; c++)
                    {
                        rgb[c] = max == 65535 ? map[c * entries + index] : (ulong)(map[c * entries + index] >> 8);
                    }
                };
            case TiffPhotometric.Separated when layout.ColorSamples == 4
                                                && layout.Directory.GetUnsigned(TiffTag.InkSet, 1) == 1:
                return (p, rgb) =>
                {
                    var k = inMax - Read(samples, p, 3);
                    for (var c = 0; c < 3; c++)
                    {
                        var ink = inMax - Read(samples, p, c);
                        rgb[c] = (ink * k * max + inMax * inMax / 2) / (inMax * inMax);
                    }
                };
            case TiffPhotometric.YCbCr:
                return YCbCr(layout, samples, max, inMax);
            case TiffPhotometric.CieLab or TiffPhotometric.IccLab:
                return Lab(layout, samples, max);
            default:
                throw new NotSupportedException($"TIFF {layout.Photometric} of {layout.ColorSamples} samples is not "
                                                + "read.");
        }
    }

    private static ColorConverter YCbCr(TiffLayout layout, TiffSamples samples, ulong max, ulong inMax)
    {
        double[] luma = layout.Directory.GetDoubleValues(TiffTag.YCbCrCoefficients) is { Length: 3 } given
            ? given
            : [0.299, 0.587, 0.114];
        var half = (inMax + 1) / 2.0;
        double[] reference = layout.Directory.GetDoubleValues(TiffTag.ReferenceBlackWhite) is { Length: 6 } set
            ? set
            : [0, inMax, half, inMax, half, inMax];
        var chromaRange = half - 1;
        return (p, rgb) =>
        {
            var y = (Read(samples, p, 0) - reference[0]) * inMax / (reference[1] - reference[0]);
            var cb = (Read(samples, p, 1) - reference[2]) * chromaRange / (reference[3] - reference[2]);
            var cr = (Read(samples, p, 2) - reference[4]) * chromaRange / (reference[5] - reference[4]);
            var r = cr * (2 - 2 * luma[0]) + y;
            var b = cb * (2 - 2 * luma[2]) + y;
            var g = (y - luma[2] * b - luma[0] * r) / luma[1];
            rgb[0] = Clamp(r / inMax, max);
            rgb[1] = Clamp(g / inMax, max);
            rgb[2] = Clamp(b / inMax, max);
        };
    }

    private static ColorConverter Lab(TiffLayout layout, TiffSamples samples, ulong max)
    {
        var icc = layout.Photometric == TiffPhotometric.IccLab;
        var wide = layout.BitsPerSample > 8;
        var lightness = wide ? 65535.0 : 255.0;
        return (p, rgb) =>
        {
            var l = Read(samples, p, 0) * 100 / lightness;
            var a = Chroma(Read(samples, p, 1), icc, wide);
            var b = Chroma(Read(samples, p, 2), icc, wide);
            var fy = (l + 16) / 116;
            var white = icc ? D50 : D65;
            var x = Inverse(fy + a / 500) * white[0];
            var y = Inverse(fy) * white[1];
            var z = Inverse(fy - b / 200) * white[2];
            if (icc)
            {
                (x, y, z) = (0.9555766 * x - 0.0230393 * y + 0.0631636 * z,
                    -0.0282895 * x + 1.0099416 * y + 0.0210077 * z,
                    0.0122982 * x - 0.0204830 * y + 1.3299098 * z);
            }

            rgb[0] = Clamp(Gamma(3.2404542 * x - 1.5371385 * y - 0.4985314 * z), max);
            rgb[1] = Clamp(Gamma(-0.9692660 * x + 1.8760108 * y + 0.0415560 * z), max);
            rgb[2] = Clamp(Gamma(0.0556434 * x - 0.2040259 * y + 1.0572252 * z), max);
        };
    }

    private static double Chroma(ulong value, bool icc, bool wide) => (icc, wide) switch
    {
        (true, false) => value - 128.0,
        (true, true) => value / 256.0 - 128,
        (false, false) => (sbyte)value,
        (false, true) => (short)value / 256.0
    };

    private static double Inverse(double t) => t > 6.0 / 29 ? t * t * t : 3 * (6.0 / 29) * (6.0 / 29) * (t - 4.0 / 29);

    private static double Gamma(double linear) =>
        linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;

    private static ulong Clamp(double unit, ulong max) => (ulong)Math.Round(Math.Clamp(unit, 0, 1) * max);

    private static TiffBitmap Floats(TiffLayout layout, TiffSamples samples)
    {
        if (layout.Photometric is not (TiffPhotometric.MinIsBlack or TiffPhotometric.MinIsWhite or TiffPhotometric.Rgb))
        {
            throw new NotSupportedException($"TIFF {layout.Photometric} in floats is not read.");
        }

        var count = (long)samples.Width * samples.Height;
        var pixels = Allocate(count * 16);
        var (alpha, associated) = Alpha(layout);
        var gray = layout.Photometric != TiffPhotometric.Rgb;
        var inverted = layout.Photometric == TiffPhotometric.MinIsWhite;
        Span<float> rgba = stackalloc float[4];
        for (long p = 0; p < count; p++)
        {
            for (var c = 0; c < 3; c++)
            {
                var value = ReadFloat(samples, p, gray ? 0 : c);
                rgba[c] = inverted ? 1 - value : value;
            }

            rgba[3] = alpha < 0 ? 1 : ReadFloat(samples, p, alpha);
            if (associated && rgba[3] != 0)
            {
                for (var c = 0; c < 3; c++)
                {
                    rgba[c] /= rgba[3];
                }
            }

            for (var c = 0; c < 4; c++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan((int)(p * 16 + c * 4)), rgba[c]);
            }
        }

        return new TiffBitmap(Format.R32G32B32A32_SFLOAT, samples.Width, samples.Height, 16, pixels);
    }

    private static (int Index, bool Associated) Alpha(TiffLayout layout)
    {
        var extra = Array.FindIndex(layout.ExtraSamples, kind => kind is 1 or 2);
        return extra < 0 ? (-1, false) : (layout.ColorSamples + extra, layout.ExtraSamples[extra] == 1);
    }

    private static byte[] Allocate(long bytes) =>
        bytes <= Array.MaxLength
            ? new byte[bytes]
            : throw new NotSupportedException($"The TIFF picture takes {bytes} bytes - more than one array holds; read "
                                              + "it by tiles instead.");

    private static ulong Scale(ulong value, ulong inMax, ulong max) =>
        inMax == max ? value : (Math.Min(value, inMax) * max + inMax / 2) / inMax;

    private static ulong Read(TiffSamples samples, long pixel, int sample)
    {
        var at = (int)((pixel * samples.SamplesPerPixel + sample) * samples.ContainerBytes);
        var data = samples.Data.AsSpan(at);
        return samples.ContainerBytes switch
        {
            1 => data[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(data),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(data),
            _ => BinaryPrimitives.ReadUInt64LittleEndian(data)
        };
    }

    private static float ReadFloat(TiffSamples samples, long pixel, int sample)
    {
        var at = (int)((pixel * samples.SamplesPerPixel + sample) * samples.ContainerBytes);
        var data = samples.Data.AsSpan(at);
        return samples.ContainerBytes switch
        {
            2 => (float)BinaryPrimitives.ReadHalfLittleEndian(data),
            4 => BinaryPrimitives.ReadSingleLittleEndian(data),
            _ => (float)BinaryPrimitives.ReadDoubleLittleEndian(data)
        };
    }

    private static TiffBitmap Orient(TiffBitmap bitmap, int orientation)
    {
        if (orientation == 1)
        {
            return bitmap;
        }

        var (w, h, size) = (bitmap.Width, bitmap.Height, bitmap.PixelBytes);
        var turned = orientation >= 5;
        var (dw, dh) = turned ? (h, w) : (w, h);
        var result = new byte[bitmap.Pixels.Length];
        for (var sy = 0; sy < h; sy++)
        {
            for (var sx = 0; sx < w; sx++)
            {
                var (dx, dy) = orientation switch
                {
                    2 => (w - 1 - sx, sy),
                    3 => (w - 1 - sx, h - 1 - sy),
                    4 => (sx, h - 1 - sy),
                    5 => (sy, sx),
                    6 => (h - 1 - sy, sx),
                    7 => (h - 1 - sy, w - 1 - sx),
                    _ => (sy, w - 1 - sx)
                };
                bitmap.Pixels.AsSpan((int)(((long)sy * w + sx) * size), size)
                    .CopyTo(result.AsSpan((int)(((long)dy * dw + dx) * size)));
            }
        }

        return bitmap with { Width = dw, Height = dh, Pixels = result };
    }
}
