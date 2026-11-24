using System;
using System.IO;
using System.IO.Compression;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Writing;

/// <summary>Pictures as image XObjects (ISO 32000-2, 8.9.5): eight bits a component, compressed, the alpha as a soft
/// mask of its own (11.6.5.3) when the picture has any. Drawn by <see cref="PdfContentBuilder.Draw"/>, filling the unit
/// square of the transformation, its first row at the top.</summary>
public static class PdfImages
{
    /// <summary>A picture of <paramref name="width"/> by <paramref name="height"/> pixels from
    /// <paramref name="rgba"/>, four bytes a pixel - red, green, blue and straight (not premultiplied) alpha - row after
    /// row from the top. <paramref name="interpolate"/> asks a reader to smooth it when it is enlarged.</summary>
    public static PdfStream FromRgba(int width, int height, ReadOnlySpan<byte> rgba, bool interpolate = true)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A picture has pixels.");
        }

        var pixels = (long)width * height;
        if (rgba.Length < pixels * 4)
        {
            throw new ArgumentException("The pixels are fewer than the picture's size needs.", nameof(rgba));
        }

        var color = new byte[pixels * 3];
        var alpha = new byte[pixels];
        var opaque = true;
        for (long index = 0; index < pixels; index++)
        {
            var from = index * 4;
            color[index * 3] = rgba[(int)from];
            color[index * 3 + 1] = rgba[(int)from + 1];
            color[index * 3 + 2] = rgba[(int)from + 2];
            alpha[index] = rgba[(int)from + 3];
            opaque &= alpha[index] == 255;
        }

        var image = Image(width, height, "DeviceRGB", color, interpolate);
        if (!opaque)
        {
            image.Dictionary.Set(PdfKeys.SMask, Image(width, height, "DeviceGray", alpha, interpolate));
        }

        return image;
    }

    private static PdfStream Image(int width, int height, string colorSpace, byte[] samples, bool interpolate)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfKeys.XObject));
        dictionary.Set(PdfKeys.Subtype, new PdfName("Image"));
        dictionary.Set(PdfKeys.Width, new PdfInteger(width));
        dictionary.Set(PdfKeys.Height, new PdfInteger(height));
        dictionary.Set(PdfKeys.ColorSpace, new PdfName(colorSpace));
        dictionary.Set(PdfKeys.BitsPerComponent, new PdfInteger(8));
        if (interpolate)
        {
            dictionary.Set(PdfKeys.Interpolate, PdfBoolean.True);
        }

        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(samples);
        }

        return new PdfStream(dictionary, output.ToArray());
    }
}
