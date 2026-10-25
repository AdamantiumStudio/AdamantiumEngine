using System;

namespace Adamantium.Imaging.Tiff;

/// <summary>The decoded samples of one TIFF image as the file means them, before any color conversion: pixel after
/// pixel, samples of a pixel together, each in a little-endian container of 1, 2, 4 or 8 bytes. Floats of 24 bits are
/// widened to 32; halves stay halves.</summary>
internal sealed class TiffSamples
{
    public TiffSamples(int width, int height, int samplesPerPixel, int containerBytes)
    {
        var size = (long)width * height * samplesPerPixel * containerBytes;
        if (size > Array.MaxLength)
        {
            throw new NotSupportedException($"The TIFF image of {width}×{height} pixels takes {size} bytes decoded - "
                                            + "more than one array holds; read it by tiles instead.");
        }

        Width = width;
        Height = height;
        SamplesPerPixel = samplesPerPixel;
        ContainerBytes = containerBytes;
        Data = new byte[size];
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Samples per pixel.</summary>
    public int SamplesPerPixel { get; }

    /// <summary>Bytes of each sample's container.</summary>
    public int ContainerBytes { get; }

    /// <summary>The samples.</summary>
    public byte[] Data { get; }

    /// <summary>The byte offset of sample <paramref name="sample"/> of pixel (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    public long Offset(int x, int y, int sample) => (((long)y * Width + x) * SamplesPerPixel + sample) * ContainerBytes;
}
