using System;
using System.IO;
using Adamantium.Imaging.Ccitt;
using Adamantium.Imaging.Jbig2;
using Adamantium.Imaging.Jpeg;
using Adamantium.Imaging.Jpeg2000;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Images;

/// <summary>Decodes images (ISO 32000-2, 8.9) to 8-bit RGBA: samples of 1 to 16 bits through <c>/Decode</c> and the
/// color space; JPEG, JPEG 2000, CCITT fax and JBIG2 through the engine's decoders; stencil masks, soft masks,
/// explicit masks and color-key masks. A color space with a tint transform or an ICC profile is evaluated on a grid,
/// not once a pixel, when the image has more pixels than the grid nodes; a JPEG 2000 image with no <c>/ColorSpace</c> is shown through the profile it holds; rows past the
/// end of the data are the color of zero samples.</summary>
public static class PdfImageDecoder
{
    /// <summary>The widest or tallest an image may be.</summary>
    public const int MaxSide = 1 << 16;

    public static PdfImageBitmap Decode(PdfStream image, PdfResources resources, PdfDocument document) =>
        Decode(image.Dictionary, document.Decode(image), resources, document, true);

    public static PdfImageBitmap Decode(PdfInlineImage image, PdfResources resources, PdfDocument document) =>
        Decode(image.Dictionary, PdfStreamDecoder.Decode(image.Dictionary, image.Data, document, document.DecodeLimits),
            resources, document, true);

    private static PdfImageBitmap Decode(PdfDictionary dictionary, PdfDecodedStream decoded, PdfResources resources,
        PdfDocument document, bool withMasks)
    {
        var width = PdfValues.Integer(dictionary[PdfKeys.Width], document);
        var height = PdfValues.Integer(dictionary[PdfKeys.Height], document);
        var imageMask = PdfValues.Boolean(dictionary[PdfKeys.ImageMask], document);

        PdfImageBitmap bitmap;
        if (decoded.ImageFilter == PdfFilterNames.DCTDecode)
        {
            var samples = JpegChannels(decoded.Data, document, out width, out height, out var channels);
            bitmap = Samples(dictionary, samples, width, height, 8, channels, resources, document, true, null);
        }
        else if (decoded.ImageFilter == PdfFilterNames.JPXDecode)
        {
            bitmap = Jpx(dictionary, decoded, resources, document, withMasks);
        }
        else
        {
            CheckSize(width, height, document);
            var data = Bilevel(decoded, width, height, document);
            var bits = decoded.ImageFilter == null
                ? PdfValues.Integer(dictionary[PdfKeys.BitsPerComponent], document, 8)
                : 1;
            bitmap = imageMask
                ? Stencil(dictionary, data.Span, width, height, document)
                : Samples(dictionary, data.Span, width, height, bits, 0, resources, document, true, null);
        }

        if (withMasks && !bitmap.IsStencil)
        {
            ApplyMasks(bitmap, dictionary, resources, document);
        }

        return bitmap;
    }

    private static void CheckSize(int width, int height, PdfDocument document)
    {
        if (width < 1 || height < 1 || width > MaxSide || height > MaxSide ||
            (long)width * height * 4 > document.DecodeLimits.MaxDecodedLength)
        {
            throw new PdfException("An image of " + width + " by " + height + " is not drawn.");
        }
    }

    private static byte[] JpegChannels(ReadOnlyMemory<byte> data, PdfDocument document, out int width,
        out int height, out int channels)
    {
        try
        {
            using var input = new MemoryStream(data.ToArray(), false);
            var decoder = new JpegDecoder(input);
            var (frameWidth, frameHeight) = decoder.Size;
            CheckSize((int)Math.Min(frameWidth, int.MaxValue), (int)Math.Max(1, Math.Min(frameHeight, int.MaxValue)),
                document);
            var samples = decoder.DecodeChannels(out width, out height, out channels);
            CheckSize(width, height, document);
            return samples;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or
                                              OverflowException or EndOfStreamException)
        {
            throw new PdfException("A JPEG image cannot be decoded: " + exception.Message, exception);
        }
    }

    private static PdfImageBitmap Stencil(PdfDictionary dictionary, ReadOnlySpan<byte> data, int width, int height,
        PdfDocument document)
    {
        var decode = PdfValues.Numbers(dictionary[PdfKeys.Decode], document);
        var paintOn = decode is { Length: >= 2 } && decode[0] > decode[1] ? 1 : 0;
        var rgba = new byte[width * height * 4];
        var rowBytes = (width + 7) / 8;
        for (var y = 0; y < height; y++)
        {
            var row = (long)y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var at = row + x / 8;
                var bit = at < data.Length ? (data[(int)at] >> (7 - x % 8)) & 1 : 1 - paintOn;
                var pixel = (y * width + x) * 4;
                rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = 255;
                rgba[pixel + 3] = bit == paintOn ? (byte)255 : (byte)0;
            }
        }

        return new PdfImageBitmap(width, height, rgba, true);
    }

    private static PdfColorSpace SpaceOf(PdfDictionary dictionary, int channels, PdfResources resources,
        PdfDocument document)
    {
        var space = PdfColorSpaces.Create(dictionary[PdfKeys.ColorSpace], resources, document);
        if (space == null || space.Components == 0 || (channels > 0 && space.Components != channels))
        {
            space = channels switch
            {
                3 => PdfDeviceRgbColorSpace.Instance,
                4 => PdfDeviceCmykColorSpace.Instance,
                _ => PdfDeviceGrayColorSpace.Instance,
            };
        }

        return space;
    }

    private static PdfImageBitmap Jpx(PdfDictionary dictionary, PdfDecodedStream decoded, PdfResources resources,
        PdfDocument document, bool withMasks)
    {
        var declared = (long)PdfValues.Integer(dictionary[PdfKeys.Width], document) *
                       PdfValues.Integer(dictionary[PdfKeys.Height], document);
        var maxPixels = Math.Min(document.DecodeLimits.MaxDecodedLength / 4,
            declared > 0 ? declared * 4 + (1 << 16) : long.MaxValue);
        Jpeg2000Image image;
        try
        {
            image = Jpeg2000Decoder.Decode(decoded.Data.Span, maxPixels);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            throw new PdfException("A JPEG 2000 image cannot be decoded: " + exception.Message, exception);
        }

        CheckSize(image.Width, image.Height, document);
        var pixels = image.Width * image.Height;
        var channels = image.Channels;
        var inData = PdfValues.Integer(dictionary[PdfKeys.SMaskInData], document);
        var colors = image.Samples;
        if (image.HasAlpha)
        {
            var premultiplied = image.AlphaIsPremultiplied || inData == 2;
            colors = new byte[pixels * image.ColorChannels];
            for (var i = 0; i < pixels; i++)
            {
                var alpha = image.Samples[i * channels + channels - 1];
                for (var c = 0; c < image.ColorChannels; c++)
                {
                    var value = image.Samples[i * channels + c];
                    colors[i * image.ColorChannels + c] = !premultiplied ? value
                        : alpha == 0 ? (byte)0
                        : (byte)Math.Min(255, value * 255 / alpha);
                }
            }
        }

        if (image.ColorChannels == 1 && image.ChannelBits[0] < 8 &&
            PdfColorSpaces.Create(dictionary[PdfKeys.ColorSpace], resources, document) is PdfIndexedColorSpace)
        {
            var max = (1 << image.ChannelBits[0]) - 1;
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = (byte)((colors[i] * max + 127) / 255);
            }
        }

        var embedded = dictionary[PdfKeys.ColorSpace] == null && image.IccProfile != null
            ? PdfIccProfiles.Of(image.IccProfile, image.ColorChannels)
            : null;
        var bitmap = Samples(dictionary, colors, image.Width, image.Height, 8, image.ColorChannels, resources,
            document, false, embedded == null ? null : new PdfIccBasedColorSpace(image.ColorChannels,
                SpaceOf(dictionary, image.ColorChannels, resources, document), null, embedded));
        if (withMasks && image.HasAlpha && inData != 0 && dictionary[PdfKeys.SMask] == null)
        {
            for (var i = 0; i < pixels; i++)
            {
                var alpha = image.Samples[i * channels + channels - 1];
                bitmap.Rgba[i * 4 + 3] = (byte)(bitmap.Rgba[i * 4 + 3] * alpha / 255);
            }
        }

        return bitmap;
    }

    private static PdfImageBitmap Samples(PdfDictionary dictionary, ReadOnlySpan<byte> data, int width, int height,
        int bits, int channels, PdfResources resources, PdfDocument document, bool readDecode, PdfColorSpace space)
    {
        space ??= SpaceOf(dictionary, channels, resources, document);
        if (bits is not (1 or 2 or 4 or 8 or 16))
        {
            bits = 8;
        }

        var rgba = new byte[width * height * 4];
        var bitmap = new PdfImageBitmap(width, height, rgba, false);
        if (space is PdfSeparationColorSpace { PaintsNothing: true })
        {
            return bitmap;
        }

        var components = space.Components;
        var decode = readDecode ? PdfValues.Numbers(dictionary[PdfKeys.Decode], document) : null;
        if (decode == null || decode.Length < 2 * components)
        {
            decode = space.DefaultDecode(bits);
        }

        var rowBytes = (int)(((long)width * components * bits + 7) / 8);
        var rowsWithData = (int)Math.Min(height, ((long)data.Length + rowBytes - 1) / rowBytes);
        var max = (1 << bits) - 1;
        var table = components == 1 && bits <= 8 ? Table(space, decode, bits) : null;
        var grid = table == null && ConvertsSlowly(space) && components <= PdfColorGrid.MaxComponents &&
                   (long)width * rowsWithData > PdfColorGrid.NodeCount(space, components)
            ? new PdfColorGrid(space, decode, components)
            : null;
        Span<double> color = stackalloc double[components];
        Span<double> rgb = stackalloc double[3];
        for (var y = 0; y < rowsWithData; y++)
        {
            var row = (long)y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                var pixel = (y * width + x) * 4;
                if (table != null)
                {
                    var sample = Sample(data, row, x, bits);
                    rgba[pixel] = table[3 * sample];
                    rgba[pixel + 1] = table[3 * sample + 1];
                    rgba[pixel + 2] = table[3 * sample + 2];
                    rgba[pixel + 3] = 255;
                    continue;
                }

                for (var c = 0; c < components; c++)
                {
                    var sample = Sample(data, row, x * components + c, bits);
                    color[c] = decode[2 * c] + sample * (decode[2 * c + 1] - decode[2 * c]) / max;
                }

                if (grid != null)
                {
                    grid.ToRgb(color, rgb);
                }
                else
                {
                    space.ToRgb(color, rgb);
                }

                rgba[pixel] = Byte(rgb[0]);
                rgba[pixel + 1] = Byte(rgb[1]);
                rgba[pixel + 2] = Byte(rgb[2]);
                rgba[pixel + 3] = 255;
            }
        }

        if (rowsWithData < height)
        {
            for (var c = 0; c < components; c++)
            {
                color[c] = decode[2 * c];
            }

            space.ToRgb(color, rgb);
            byte r = Byte(rgb[0]), g = Byte(rgb[1]), b = Byte(rgb[2]);
            for (var pixel = rowsWithData * width * 4; pixel < rgba.Length; pixel += 4)
            {
                rgba[pixel] = r;
                rgba[pixel + 1] = g;
                rgba[pixel + 2] = b;
                rgba[pixel + 3] = 255;
            }
        }

        if (document.Resolve(dictionary[PdfKeys.Mask]) is PdfArray colorKey)
        {
            ApplyColorKey(bitmap, data, rowBytes, rowsWithData, components, bits, colorKey, document);
        }

        return bitmap;
    }

    private static bool ConvertsSlowly(PdfColorSpace space) =>
        space is PdfSeparationColorSpace or PdfDeviceNColorSpace ||
        (space is PdfIccBasedColorSpace icc && (icc.IsManaged || ConvertsSlowly(icc.Alternate)));

    private static byte[] Table(PdfColorSpace space, double[] decode, int bits)
    {
        var count = 1 << bits;
        var max = count - 1;
        var table = new byte[3 * count];
        Span<double> color = stackalloc double[1];
        Span<double> rgb = stackalloc double[3];
        for (var sample = 0; sample < count; sample++)
        {
            color[0] = decode[0] + sample * (decode[1] - decode[0]) / max;
            space.ToRgb(color, rgb);
            table[3 * sample] = Byte(rgb[0]);
            table[3 * sample + 1] = Byte(rgb[1]);
            table[3 * sample + 2] = Byte(rgb[2]);
        }

        return table;
    }

    private static int Sample(ReadOnlySpan<byte> data, long row, int index, int bits)
    {
        switch (bits)
        {
            case 8:
                var at = row + index;
                return at < data.Length ? data[(int)at] : 0;
            case 16:
                var wide = row + 2L * index;
                return wide + 1 < data.Length ? (data[(int)wide] << 8) | data[(int)wide + 1] : 0;
            default:
                var bit = (long)index * bits;
                var position = row + (bit >> 3);
                if (position >= data.Length)
                {
                    return 0;
                }

                var shift = 8 - bits - (int)(bit & 7);
                return (data[(int)position] >> shift) & ((1 << bits) - 1);
        }
    }

    private static void ApplyColorKey(PdfImageBitmap bitmap, ReadOnlySpan<byte> data, int rowBytes, int rowsWithData,
        int components, int bits, PdfArray colorKey, PdfDocument document)
    {
        if (colorKey.Count < 2 * components)
        {
            return;
        }

        Span<int> low = stackalloc int[components];
        Span<int> high = stackalloc int[components];
        var zeroMasked = true;
        for (var c = 0; c < components; c++)
        {
            low[c] = PdfValues.Integer(colorKey[2 * c], document);
            high[c] = PdfValues.Integer(colorKey[2 * c + 1], document);
            zeroMasked &= low[c] <= 0 && high[c] >= 0;
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var masked = y >= rowsWithData ? zeroMasked : true;
                for (var c = 0; c < components && masked && y < rowsWithData; c++)
                {
                    var sample = Sample(data, (long)y * rowBytes, x * components + c, bits);
                    masked = sample >= low[c] && sample <= high[c];
                }

                if (masked)
                {
                    bitmap.Rgba[(y * bitmap.Width + x) * 4 + 3] = 0;
                }
            }
        }
    }

    private static void ApplyMasks(PdfImageBitmap bitmap, PdfDictionary dictionary, PdfResources resources,
        PdfDocument document)
    {
        PdfImageBitmap mask = null;
        var soft = false;
        try
        {
            if (document.Resolve(dictionary[PdfKeys.SMask]) is PdfStream softMask)
            {
                mask = Decode(softMask.Dictionary, document.Decode(softMask), resources, document, false);
                soft = true;
            }
            else if (document.Resolve(dictionary[PdfKeys.Mask]) is PdfStream stencilMask)
            {
                mask = StencilMask(stencilMask, document);
            }
        }
        catch (PdfException)
        {
            return;
        }

        if (mask == null)
        {
            return;
        }

        for (var y = 0; y < bitmap.Height; y++)
        {
            var my = (int)((long)y * mask.Height / bitmap.Height);
            for (var x = 0; x < bitmap.Width; x++)
            {
                var mx = (int)((long)x * mask.Width / bitmap.Width);
                var source = (my * mask.Width + mx) * 4;
                var alpha = soft ? mask.Rgba[source] : mask.Rgba[source + 3];
                var target = (y * bitmap.Width + x) * 4 + 3;
                bitmap.Rgba[target] = (byte)(bitmap.Rgba[target] * alpha / 255);
            }
        }
    }

    /// <summary>An explicit mask is a stencil whether it says <c>/ImageMask</c> or not: one bit a sample, 0 painted.
    /// </summary>
    private static PdfImageBitmap StencilMask(PdfStream stream, PdfDocument document)
    {
        var decoded = document.Decode(stream);
        var width = PdfValues.Integer(stream.Dictionary[PdfKeys.Width], document);
        var height = PdfValues.Integer(stream.Dictionary[PdfKeys.Height], document);
        CheckSize(width, height, document);
        return Stencil(stream.Dictionary, Bilevel(decoded, width, height, document).Span, width, height, document);
    }

    private static ReadOnlyMemory<byte> Bilevel(PdfDecodedStream decoded, int width, int height,
        PdfDocument document)
    {
        switch (decoded.ImageFilter)
        {
            case null:
                return decoded.Data;
            case PdfFilterNames.CCITTFaxDecode:
                return Fax(decoded, height, document);
            case PdfFilterNames.JBIG2Decode:
                return Jbig2(decoded, width, height, document);
            default:
                throw new PdfException("/" + decoded.ImageFilter + " images are not read yet.");
        }
    }

    private static byte[] Fax(PdfDecodedStream decoded, int height, PdfDocument document)
    {
        var parameters = decoded.ImageFilterParameters;
        var options = new CcittOptions
        {
            K = PdfValues.Integer(parameters?[PdfKeys.K], document),
            Columns = PdfValues.Integer(parameters?[PdfKeys.Columns], document, 1728),
            EncodedByteAlign = PdfValues.Boolean(parameters?[PdfKeys.EncodedByteAlign], document),
            BlackIs1 = PdfValues.Boolean(parameters?[PdfKeys.BlackIs1], document),
        };
        var rowBytes = (options.Columns + 7) / 8;
        if (options.Columns is < 1 or > MaxSide || (long)rowBytes * height > document.DecodeLimits.MaxDecodedLength)
        {
            throw new PdfException("A fax image of " + options.Columns + " columns is not drawn.");
        }

        var rows = new byte[rowBytes * height];
        CcittDecoder.Decode(decoded.Data.Span, rows, options);
        return rows;
    }

    private static byte[] Jbig2(PdfDecodedStream decoded, int width, int height, PdfDocument document)
    {
        var globals = ReadOnlyMemory<byte>.Empty;
        if (document.Resolve(decoded.ImageFilterParameters?[PdfKeys.JBIG2Globals]) is PdfStream shared)
        {
            globals = document.Decode(shared).Data;
        }

        byte[] page;
        int pageWidth, pageHeight;
        try
        {
            page = Jbig2Decoder.Decode(decoded.Data.Span, globals.Span,
                Math.Min(document.DecodeLimits.MaxDecodedLength, (long)width * height * 4 + (1 << 16)),
                out pageWidth, out pageHeight);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            throw new PdfException("A JBIG2 image cannot be decoded: " + exception.Message, exception);
        }

        var rowBytes = (width + 7) / 8;
        var pageRowBytes = (pageWidth + 7) / 8;
        var rows = new byte[rowBytes * height];
        Array.Fill(rows, (byte)0xFF);
        var copied = Math.Min(rowBytes, pageRowBytes);
        for (var y = 0; y < Math.Min(height, pageHeight); y++)
        {
            for (var i = 0; i < copied; i++)
            {
                rows[y * rowBytes + i] = (byte)~page[y * pageRowBytes + i];
            }
        }

        return rows;
    }

    private static byte Byte(double value) =>
        (byte)(value <= 0 ? 0 : value >= 1 ? 255 : (int)(value * 255 + 0.5));
}
