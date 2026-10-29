using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Jpeg;

/// <summary>Decodes a JPEG (ITU-T T.81, JFIF): baseline, extended and progressive, Huffman- or arithmetic-coded, 8 or
/// 12 bits, gray, YCbCr, RGB,
/// CMYK and YCCK, any subsampling whose factors divide the largest. The picture comes out RGBA - 8 bits a channel, 16
/// for 12-bit samples.</summary>
public sealed class JpegDecoder
{
    private readonly byte[] _data;

    /// <summary>A decoder of the JPEG in <paramref name="input"/>, read from its position to its end.</summary>
    public JpegDecoder(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        _data = copy.ToArray();
    }

    /// <summary>Whether three components are red, green and blue as they are - as TIFF stores JPEG of RGB data -
    /// rather than YCbCr to convert.</summary>
    public bool ComponentsAreRgb { get; set; }

    /// <summary>The picture's size as its frame header gives it, decoding nothing - what <see cref="DecodePreview"/>'s
    /// eighth was taken of. A height of 0 for a file that gives its lines only after the first scan (DNL); 0 × 0
    /// without a frame.</summary>
    public (uint Width, uint Height) Size
    {
        get
        {
            var (width, height, _) = JpegStream.FrameSize(_data);
            return ((uint)width, (uint)height);
        }
    }

    /// <summary>The picture.</summary>
    public JpegImage Decode() => Picture(false);

    /// <summary>The picture at an eighth of its size, rounded up, from each block's DC term alone: no inverse DCT,
    /// and a progressive file's AC-only scans not decoded at all - a thumbnail, a preview, a backdrop to blur.</summary>
    public JpegImage DecodePreview() => Picture(true);

    private JpegImage Picture(bool preview)
    {
        var stream = JpegStream.Decode(_data, preview);
        try
        {
            var model = JpegColor.ModelOf(stream, ComponentsAreRgb);
            var (width, height) = JpegColor.SizeOf(stream);
            var wide = stream.Precision > 8;
            var pixelBytes = wide ? 8 : 4;
            if ((long)width * height * pixelBytes > Array.MaxLength)
            {
                throw new NotSupportedException($"A JPEG of {width}×{height} is more than one array holds.");
            }

            var pixels = new byte[width * height * pixelBytes];
            JpegColor.Write(stream, model, true, pixels, width * pixelBytes, height);
            return new JpegImage((uint)width, (uint)height,
                wide ? SurfaceFormat.R16G16B16A16.UNorm : SurfaceFormat.R8G8B8A8.UNorm, pixels);
        }
        finally
        {
            stream.Release();
        }
    }

    /// <summary>Decodes <paramref name="data"/> into <paramref name="output"/> as its own channels - gray, RGB, or
    /// CMYK - of 1 byte, or 2 little-endian for 12-bit samples, rows <paramref name="stride"/> bytes apart; three
    /// components are RGB as they are when <paramref name="rgb"/>; rows past <paramref name="rows"/> are left out.
    /// The stream's tables start as <paramref name="tables"/>' (<see cref="JpegStream.Tables"/>), when given.
    /// Returns the picture's size, channels and bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static (int Width, int Height, int Channels, int Precision) DecodeSamples(ReadOnlySpan<byte> data,
        JpegStream tables, bool rgb, Span<byte> output, int stride, int rows)
    {
        var stream = JpegStream.Decode(data, false, tables, false);
        try
        {
            var model = JpegColor.ModelOf(stream, rgb);
            var channels = model switch
            {
                JpegColorModel.Gray => 1,
                JpegColorModel.Rgb or JpegColorModel.YCbCr => 3,
                _ => 4
            };
            JpegColor.Write(stream, model, false, output, stride, rows);
            return (stream.Width, stream.Height, channels, stream.Precision);
        }
        finally
        {
            stream.Release();
        }
    }
}
