using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Png;

namespace Adamantium.Imaging.Ico
{
    public static class IcoHelper
    {
        private const int DirectorySize = 6;
        private const int EntrySize = 16;
        private const int BitmapHeaderSize = 40;
        private const int PngDepth = 32;

        private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        /// <summary>Every image of an ICO file, PNG or uncompressed bitmap, as RGBA pixels top-down: the largest is the
        /// first level, and of images of one size the one of most colors comes first.</summary>
        public static unsafe IRawBitmap LoadFromMemory(IntPtr pSource, ulong size)
        {
            var data = new ReadOnlySpan<byte>((void*)pSource, checked((int)size));
            if (data.Length < DirectorySize || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != 1)
            {
                throw new ArgumentException("Given file is not an ICO file");
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            if (count == 0 || data.Length < DirectorySize + count * EntrySize)
            {
                throw new ArgumentException("The ICO file lists no images, or more than it holds");
            }

            var frames = new List<(FrameData Frame, int Depth)>(count);
            for (var index = 0; index < count; index++)
            {
                var entry = data.Slice(DirectorySize + index * EntrySize, EntrySize);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
                if (offset > (uint)data.Length || length > (uint)data.Length - offset)
                {
                    throw new ArgumentException($"Image {index} of the ICO file lies outside it");
                }

                var image = data.Slice((int)offset, (int)length);
                frames.Add(image.StartsWith(PngSignature)
                    ? (DecodePng(IntPtr.Add(pSource, (int)offset), length), PngDepth)
                    : (DecodeBitmap(image), BinaryPrimitives.ReadUInt16LittleEndian(image[14..])));
            }

            frames.Sort((a, b) =>
            {
                var bySize = Area(b.Frame).CompareTo(Area(a.Frame));
                return bySize != 0 ? bySize : b.Depth.CompareTo(a.Depth);
            });
            var ico = new IcoImage(frames[0].Frame.Description);
            for (var level = 0; level < frames.Count; level++)
            {
                frames[level].Frame.MipLevel = (uint)level;
                ico.AddMipLevel(frames[level].Frame);
            }

            return ico;
        }

        private static long Area(FrameData frame) => (long)frame.Description.Width * frame.Description.Height;

        private static FrameData DecodePng(IntPtr source, uint length)
        {
            var png = PngHelper.LoadFromMemory(source, length);
            var rgba = png.GetRgba8Pixels()
                       ?? throw new NotSupportedException($"An ICO image in PNG of {png.PixelFormat} pixels.");
            return new FrameData(rgba, ImageDescription.Default2D(png.Width, png.Height, SurfaceFormat.R8G8B8A8.UNorm));
        }

        private static FrameData DecodeBitmap(ReadOnlySpan<byte> image)
        {
            if (image.Length < BitmapHeaderSize)
            {
                throw new ArgumentException("An ICO image is neither a PNG nor a bitmap");
            }

            var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(image);
            var width = BinaryPrimitives.ReadInt32LittleEndian(image[4..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(image[8..]) / 2;
            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(image[14..]);
            var compression = BinaryPrimitives.ReadUInt32LittleEndian(image[16..]);
            var colorsUsed = (int)BinaryPrimitives.ReadUInt32LittleEndian(image[32..]);
            if (width <= 0 || height <= 0 || compression != 0 || bitCount is not (1 or 4 or 8 or 24 or 32))
            {
                throw new NotSupportedException($"An ICO bitmap of {bitCount} bits, compression {compression}, is not read");
            }

            var paletteSize = bitCount <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bitCount) : 0;
            var palette = image[headerSize..];
            var colorStride = (width * bitCount + 31) / 32 * 4;
            var maskStride = (width + 31) / 32 * 4;
            var colors = palette[(paletteSize * 4)..];
            var mask = colors[(colorStride * height)..];
            if (mask.Length < maskStride * height)
            {
                mask = ReadOnlySpan<byte>.Empty;
            }

            var rgba = new byte[width * height * 4];
            var alphaSeen = false;
            for (var y = 0; y < height; y++)
            {
                var row = colors.Slice((height - 1 - y) * colorStride, colorStride);
                for (var x = 0; x < width; x++)
                {
                    var to = (y * width + x) * 4;
                    if (bitCount >= 24)
                    {
                        var from = x * (bitCount / 8);
                        rgba[to] = row[from + 2];
                        rgba[to + 1] = row[from + 1];
                        rgba[to + 2] = row[from];
                        rgba[to + 3] = bitCount == 32 ? row[from + 3] : byte.MaxValue;
                        alphaSeen |= bitCount == 32 && row[from + 3] != 0;
                    }
                    else
                    {
                        var bit = x * bitCount;
                        var index = (row[bit / 8] >> (8 - bitCount - bit % 8)) & ((1 << bitCount) - 1);
                        var color = palette.Slice(Math.Min(index, paletteSize - 1) * 4, 4);
                        rgba[to] = color[2];
                        rgba[to + 1] = color[1];
                        rgba[to + 2] = color[0];
                        rgba[to + 3] = byte.MaxValue;
                    }
                }
            }

            if (!mask.IsEmpty && (bitCount < 32 || !alphaSeen))
            {
                for (var y = 0; y < height; y++)
                {
                    var row = mask.Slice((height - 1 - y) * maskStride, maskStride);
                    for (var x = 0; x < width; x++)
                    {
                        var transparent = (row[x / 8] >> (7 - x % 8) & 1) != 0;
                        rgba[(y * width + x) * 4 + 3] = transparent ? (byte)0 : byte.MaxValue;
                    }
                }
            }

            return new FrameData(rgba, ImageDescription.Default2D((uint)width, (uint)height, SurfaceFormat.R8G8B8A8.UNorm));
        }

        /// <summary>Writes every level of <paramref name="img"/> - RGBA pixels, at most 256 a side - as an ICO file of PNG
        /// images.</summary>
        public static void SaveToStream(IRawBitmap img, Stream imageStream)
        {
            var levels = Math.Max(1u, img.MipLevelsCount);
            var images = new List<(uint Width, uint Height, byte[] Png)>((int)levels);
            for (uint level = 0; level < levels; level++)
            {
                var frame = img.MipLevelsCount > 1 ? img.GetMipLevelData(level) : img.GetFrameData(0);
                var (width, height) = (frame.Description.Width, frame.Description.Height);
                if (frame.Description.Format != SurfaceFormat.R8G8B8A8.UNorm || width is 0 or > 256 || height is 0 or > 256)
                {
                    throw new NotSupportedException("An ICO image is written from RGBA pixels, at most 256 a side");
                }

                using var image = Image.New2D(width, height, SurfaceFormat.R8G8B8A8.UNorm);
                System.Runtime.InteropServices.Marshal.Copy(frame.RawPixels, 0, image.DataPointer, (int)(width * height * 4));
                using var png = new MemoryStream();
                image.Save(png, ImageFileType.Png);
                images.Add((width, height, png.ToArray()));
            }

            Span<byte> header = stackalloc byte[DirectorySize + EntrySize];
            BinaryPrimitives.WriteUInt16LittleEndian(header, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)images.Count);
            imageStream.Write(header[..DirectorySize]);

            var offset = (uint)(DirectorySize + EntrySize * images.Count);
            var entry = header[DirectorySize..];
            foreach (var (width, height, png) in images)
            {
                entry.Clear();
                entry[0] = (byte)(width == 256 ? 0 : width);
                entry[1] = (byte)(height == 256 ? 0 : height);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)png.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], offset);
                imageStream.Write(entry);
                offset += (uint)png.Length;
            }

            foreach (var (_, _, png) in images)
            {
                imageStream.Write(png);
            }
        }
    }
}
