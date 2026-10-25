using System;

namespace Adamantium.Imaging.Tiff;

/// <summary>Reads one TIFF image whole into its samples, strip by strip or tile by tile.</summary>
internal static class TiffDecoder
{
    /// <summary>Decodes the whole image of <paramref name="layout"/>.</summary>
    public static TiffSamples Decode(ITiffSource source, TiffLayout layout, bool littleEndian)
    {
        var decoder = new TiffChunkDecoder(source, layout, littleEndian);
        if (layout.Compression == TiffCompression.None)
        {
            decoder.CheckUncompressed();
        }

        var samples = new TiffSamples(layout.Width, layout.Height, layout.SamplesPerPixel, decoder.Container);
        var native = new byte[decoder.NativeChunkBytes];
        var rowBytes = (int)decoder.NativeRowBytes;
        var planes = layout.IsPlanar ? layout.SamplesPerPixel : 1;
        for (var plane = 0; plane < planes; plane++)
        {
            for (var down = 0; down < layout.ChunksDown; down++)
            {
                for (var across = 0; across < layout.ChunksAcross; across++)
                {
                    if (!decoder.Holds(across, down, plane))
                    {
                        continue;
                    }

                    decoder.Decode(across, down, plane, native);
                    var x0 = across * layout.ChunkWidth;
                    var columns = Math.Min(layout.ChunkWidth, layout.Width - x0);
                    for (var r = 0; r < layout.ChunkRows(down); r++)
                    {
                        var row = native.AsSpan(r * rowBytes, rowBytes);
                        var y = down * layout.ChunkHeight + r;
                        if (!layout.IsPlanar)
                        {
                            row[..(columns * decoder.Samples * decoder.Container)]
                                .CopyTo(samples.Data.AsSpan((int)samples.Offset(x0, y, 0)));
                            continue;
                        }

                        for (var x = 0; x < columns; x++)
                        {
                            row.Slice(x * decoder.Container, decoder.Container)
                                .CopyTo(samples.Data.AsSpan((int)samples.Offset(x0 + x, y, plane)));
                        }
                    }
                }
            }
        }

        return samples;
    }
}
