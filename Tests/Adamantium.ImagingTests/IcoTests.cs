using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// ICO files as applications carry them: PNG images up to 256 pixels a side, older bitmap images with a palette and a
/// transparency mask. Found when the window icon came to Linux: every image of the applications' icons is a PNG, which
/// was read as a bitmap, and the 256-pixel one - its size written as 0 - was skipped.
/// </summary>
[TestFixture]
public class IcoTests
{
    private static byte[] Pattern(uint size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                pixels[i] = (byte)(x * 7);
                pixels[i + 1] = (byte)(y * 3);
                pixels[i + 2] = (byte)(x ^ y);
                pixels[i + 3] = (byte)(x + y);
            }
        }

        return pixels;
    }

    private static byte[] Png(uint size, byte[] pixels)
    {
        using var image = Image.New2D(size, size, 1, SurfaceFormat.R8G8B8A8.UNorm);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, image.PixelBuffer[0].DataPointer, pixels.Length);
        var stream = new MemoryStream();
        BitmapLoader.Save(image.ConvertToRawBitmap(), stream, ImageFileType.Png);
        return stream.ToArray();
    }

    private static byte[] Ico(params (int Size, byte[] Data)[] images)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);
        var offset = 6 + 16 * images.Length;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
        {
            writer.Write(data);
        }

        return stream.ToArray();
    }

    private static byte[] Bitmap(int size, ushort bitCount, byte[] palette, byte[] colorRows, byte[] maskRows, uint compression = 0)
    {
        var header = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), size);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), compression);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(32), palette.Length / 4);
        return [.. header, .. palette, .. colorRows, .. maskRows];
    }

    private static IRawBitmap Load(byte[] ico) => BitmapLoader.Load(new MemoryStream(ico));

    [Test]
    public void PngImages_AreReadAtEverySize_The256OneFirst()
    {
        var small = Pattern(16);
        var large = Pattern(256);

        var icon = Load(Ico((16, Png(16, small)), (256, Png(256, large))));

        Assert.That(icon.MipLevelsCount, Is.EqualTo(2));
        Assert.That((icon.Width, icon.Height), Is.EqualTo((256u, 256u)));
        Assert.That(icon.GetMipLevelData(0).RawPixels, Is.EqualTo(large));
        Assert.That(icon.GetMipLevelData(1).Description.Width, Is.EqualTo(16u));
        Assert.That(icon.GetMipLevelData(1).RawPixels, Is.EqualTo(small));
    }

    [Test]
    public void A32BitBitmap_IsReadTopDown_WithItsOwnAlpha()
    {
        // Rows bottom-up, pixels blue-green-red-alpha.
        byte[] rows =
        [
            0, 0, 255, 255, 0, 255, 0, 128,
            255, 0, 0, 64, 1, 2, 3, 0,
        ];
        var mask = new byte[2 * 4];

        var icon = Load(Ico((2, Bitmap(2, 32, [], rows, mask))));

        Assert.That(icon.GetMipLevelData(0).RawPixels, Is.EqualTo(new byte[]
        {
            0, 0, 255, 64, 3, 2, 1, 0,
            255, 0, 0, 255, 0, 255, 0, 128,
        }));
    }

    [Test]
    public void AFourBitBitmap_TakesItsPalette_AndItsMaskMakesPixelsClear()
    {
        byte[] palette = [0, 0, 0, 0, 255, 0, 0, 0, 0, 0, 255, 0];
        // Two rows of two pixels, bottom-up, each row padded to 4 bytes: bottom = 1, 2; top = 2, 0.
        byte[] rows = [0x12, 0, 0, 0, 0x20, 0, 0, 0];
        // The mask, bottom-up, 4 bytes a row: the top-right pixel is clear.
        byte[] mask = [0, 0, 0, 0, 0x40, 0, 0, 0];

        var icon = Load(Ico((2, Bitmap(2, 4, palette, rows, mask))));

        Assert.That(icon.GetMipLevelData(0).RawPixels, Is.EqualTo(new byte[]
        {
            255, 0, 0, 255, 0, 0, 0, 0,
            0, 0, 255, 255, 255, 0, 0, 255,
        }));
    }

    [Test]
    public void A24BitBitmap_IsOpaque()
    {
        // One row of two pixels, blue-green-red, padded to 8 bytes; the mask clears nothing.
        byte[] rows = [0, 0, 255, 0, 255, 0, 0, 0];
        var icon = Load(Ico((2, Bitmap(2, 24, [], [.. rows, .. rows], new byte[8]))));

        Assert.That(icon.GetMipLevelData(0).RawPixels[..8], Is.EqualTo(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }));
    }

    [Test]
    public void OneAndEightBitBitmaps_TakeTheirPalette()
    {
        byte[] palette = [0, 0, 0, 0, 0, 255, 0, 0];
        // 1 bit: the top row is pixel 1 then 0, the bottom row 0 then 1 - rows bottom-up, 4 bytes each.
        var oneBit = Load(Ico((2, Bitmap(2, 1, palette, [0x40, 0, 0, 0, 0x80, 0, 0, 0], new byte[8]))));
        // 8 bits: the same picture, a byte a pixel.
        var eightBit = Load(Ico((2, Bitmap(2, 8, palette, [0, 1, 0, 0, 1, 0, 0, 0], new byte[8]))));

        byte[] expected = [0, 255, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 255, 0, 255];
        Assert.That(oneBit.GetMipLevelData(0).RawPixels, Is.EqualTo(expected));
        Assert.That(eightBit.GetMipLevelData(0).RawPixels, Is.EqualTo(expected));
    }

    [Test]
    public void OfImagesOfOneSize_TheOneOfMostColorsComesFirst()
    {
        byte[] black = [0, 0, 0, 0];
        var fourBit = Bitmap(2, 4, black, [0, 0, 0, 0, 0, 0, 0, 0], new byte[8]);
        var red = Bitmap(2, 32, [], [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255], new byte[8]);

        var icon = Load(Ico((2, fourBit), (2, red)));

        Assert.That(icon.GetMipLevelData(0).RawPixels[..4], Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
    }

    [Test]
    public void ABitmapOfColorMasks_IsRefused_NotReadShifted()
    {
        var masked = Bitmap(1, 32, [], [0, 0, 0, 255, 0, 255, 0, 0, 255, 0, 0, 0, 0, 0, 255, 255], new byte[4], compression: 3);

        Assert.Throws<NotSupportedException>(() => Load(Ico((1, masked))));
    }

    [Test]
    public void AWrittenIcon_ReadsBackTheSame()
    {
        var levels = new List<byte[]> { Pattern(256), Pattern(32) };
        var source = new Adamantium.Imaging.Ico.IcoImage(ImageDescription.Default2D(256, 256, SurfaceFormat.R8G8B8A8.UNorm));
        source.AddMipLevel(new FrameData(levels[0], ImageDescription.Default2D(256, 256, SurfaceFormat.R8G8B8A8.UNorm), 0));
        source.AddMipLevel(new FrameData(levels[1], ImageDescription.Default2D(32, 32, SurfaceFormat.R8G8B8A8.UNorm), 1));

        var stream = new MemoryStream();
        BitmapLoader.Save(source, stream, ImageFileType.Ico);
        var written = stream.ToArray();
        var icon = Load(written);

        Assert.That(written[6], Is.Zero, "a 256-pixel image is listed with the size 0");
        Assert.That(icon.MipLevelsCount, Is.EqualTo(2));
        Assert.That(icon.GetMipLevelData(0).RawPixels, Is.EqualTo(levels[0]));
        Assert.That(icon.GetMipLevelData(1).RawPixels, Is.EqualTo(levels[1]));
    }

    [Test]
    public void AFileThatListsMoreThanItHolds_IsRefused()
    {
        var ico = Ico((16, Png(16, Pattern(16))));
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(6 + 8), 1_000_000);

        Assert.Throws<ArgumentException>(() => Load(ico));
    }
}
