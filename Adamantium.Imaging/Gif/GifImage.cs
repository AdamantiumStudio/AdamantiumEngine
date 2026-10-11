using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Gif
{
    public class GifImage : IRawBitmap
    {
        private readonly List<GifFrame> frames;
        private readonly object _decodeSync = new();
        private readonly Walk _decoding = new();
        private readonly Walk _copying = new();

        private sealed class Walk
        {
            public int At = -1;
            public int[] Index;
            public byte[] Scratch;

            public void Reset()
            {
                At = -1;
                Index = null;
                Scratch = null;
            }
        }

        public GifImage()
        {
            frames = new List<GifFrame>();
            GlobalColorTable = new List<ColorRGB>();
        }
        
        public List<ColorRGB> GlobalColorTable { get; internal set; }

        public IReadOnlyList<GifFrame> Frames => frames;

        public GifFrame CurrentFrame { get; internal set; }

        public byte ColorDepth { get; internal set; }

        public ScreenDescriptor Descriptor { get; set; }
        
        public ApplicationExtension ApplicationExtension { get; set; }

        public void AddFrame(GifFrame frame)
        {
            frames.Add(frame);
        }
        
        public byte[] DecodeFrame(uint frameIndex)
        {
            lock (_decodeSync)
            {
                var index = Clamp(frameIndex);
                var frame = frames[index];
                if (!frame.IsDecoded)
                {
                    var pixels = new byte[PixelBytes(frame, (uint)index)];
                    Compose(_decoding, index, pixels);
                    frame.RawPixels = pixels;
                    frame.IsDecoded = true;
                }

                return frame.RawPixels;
            }
        }

        /// <summary>Writes one frame's pixels into <paramref name="destination"/> without keeping them: frames read in order
        /// cost one buffer between them instead of one each. What the frame does not cover is cleared.</summary>
        public void CopyFrame(uint frameIndex, Span<byte> destination)
        {
            lock (_decodeSync)
            {
                var index = Clamp(frameIndex);
                var frame = frames[index];
                var length = frame.IsDecoded ? frame.RawPixels.Length : PixelBytes(frame, (uint)index);
                if (frame.IsDecoded)
                {
                    frame.RawPixels.CopyTo(destination);
                }
                else
                {
                    Compose(_copying, index, destination[..length]);
                }

                destination[length..].Clear();
            }
        }

        private int Clamp(uint frameIndex) => (int)Math.Min(frameIndex, (uint)frames.Count - 1);

        private bool ComposesWholeFrame(GifFrame frame, uint frameIndex) =>
            frameIndex == 0 ||
            (frame.Descriptor.Width == Descriptor.Width && frame.Descriptor.Height == Descriptor.Height) ||
            (frame.GraphicControlExtension != null && frame.GraphicControlExtension.DisposalMethod == DisposalMethod.None);

        private int PixelBytes(GifFrame frame, uint frameIndex) => ComposesWholeFrame(frame, frameIndex)
            ? frame.Descriptor.Width * frame.Descriptor.Height * 4
            : Descriptor.Width * Descriptor.Height * 4;

        private void Compose(Walk walk, int index, Span<byte> pixels)
        {
            if (index <= walk.At)
            {
                walk.At = -1;
                walk.Index = null;
            }

            for (var i = walk.At + 1; i <= index; i++)
            {
                var f = frames[i];
                var decoded = LZW.Decompress(CollectionsMarshal.AsSpan(f.CompressedData), f.LzwMinimumCodeSize,
                    f.Descriptor.Width * f.Descriptor.Height);
                var target = pixels;
                if (i < index)
                {
                    var bytes = PixelBytes(f, (uint)i);
                    if (walk.Scratch == null || walk.Scratch.Length < bytes)
                    {
                        walk.Scratch = new byte[bytes];
                    }

                    target = walk.Scratch.AsSpan(0, bytes);
                }

                walk.Index = GetImageFromIndexStream(f, (uint)i, target, decoded, walk.Index);
                walk.At = i;
            }
        }

        private int[] GetImageFromIndexStream(GifFrame frame, uint frameIndex, Span<byte> pixels, int[] indexData, int[] previous)
        {
            var width = frame.Descriptor.Width;
            var height = frame.Descriptor.Height;
            var colorTable = frame.ColorTable;

            int offset = 0;
            int bytesPerPixel = 4;

            if (frame.Interlaced)
            {
                indexData = GifDecoder.Deinterlace(frame, indexData);
            }

            if (ComposesWholeFrame(frame, frameIndex))
            {
                for (int i = 0; i < width * height; i++)
                {
                    var colors = colorTable[indexData[i]];
                    pixels[offset] = colors.R;
                    pixels[offset + 1] = colors.G;
                    pixels[offset + 2] = colors.B;
                    if (frame.GraphicControlExtension != null &&
                        frame.GraphicControlExtension.TransparentColorIndex == i)
                    {
                        pixels[offset + 3] = 0;
                    }
                    else
                    {
                        pixels[offset + 3] = 255;
                    }

                    offset += bytesPerPixel;
                }

                return indexData;
            }
            else if (frameIndex > 0 &&
                     (frame.Descriptor.OffsetLeft != Descriptor.Width || frame.Descriptor.OffsetTop != Descriptor.Height))
                     //frame.GraphicControlExtension != null && 
                     //frame.GraphicControlExtension.DisposalMethod == DisposalMethod.DoNotDispose)
            {
                var baseFrame = Frames[(int)frameIndex - 1];
                var canvas = previous;
                var canvasWidth = (int)Descriptor.Width;
                var left = (int)frame.Descriptor.OffsetLeft;
                var top = (int)frame.Descriptor.OffsetTop;
                var frameWidth = (int)frame.Descriptor.Width;
                var frameHeight = (int)frame.Descriptor.Height;

                int transparentIndex = 0;
                bool transparencyAvailable = false;
                if (baseFrame.GraphicControlExtension != null && baseFrame.GraphicControlExtension.TransparencyAvailable)
                {
                    transparencyAvailable = baseFrame.GraphicControlExtension.TransparencyAvailable;
                    transparentIndex = baseFrame.GraphicControlExtension.TransparentColorIndex;
                }

                for (int i = 0; i < canvas.Length; i++)
                {
                    var y = i / canvasWidth;
                    var x = i - y * canvasWidth;
                    var original = canvas[i];
                    var value = original;
                    if (y >= top && y < top + frameHeight && x >= left && x < left + frameWidth)
                    {
                        value = indexData[(y - top) * frameWidth + (x - left)];
                    }

                    bool useBaseColorTable = false;
                    if (value == transparentIndex && transparencyAvailable)
                    {
                        value = original;
                        useBaseColorTable = true;
                    }

                    canvas[i] = value;
                    var colors = useBaseColorTable ? baseFrame.ColorTable[value] : colorTable[value];

                    pixels[offset] = colors.R;
                    pixels[offset + 1] = colors.G;
                    pixels[offset + 2] = colors.B;
                    if (frame.GraphicControlExtension != null &&
                        frame.GraphicControlExtension.TransparentColorIndex == i)
                    {
                        pixels[offset + 3] = 0;
                    }
                    else
                    {
                        pixels[offset + 3] = 255;
                    }
                    offset += bytesPerPixel;
                }

                return canvas;
            }
            else
            {
                throw new NotImplementedException($"Current disposal method: {frame.GraphicControlExtension?.DisposalMethod} is not supported");
            }
        }

        public uint Width => Descriptor.Width;

        public uint Height => Descriptor.Height;

        public SurfaceFormat PixelFormat => SurfaceFormat.R8G8B8A8.UNorm;
        public bool IsMultiFrame => frames.Count > 0;
        public bool HasMipLevels => false;
        public uint MipLevelsCount => 0;
        public uint NumberOfReplays => 0;
        public uint FramesCount => (uint)frames.Count;
        public byte[] GetRawPixels(uint frameIndex)
        {
            return DecodeFrame(frameIndex);
        }

        public FrameData GetMipLevelData(uint mipLevel)
        {
            return GetFrameData(0);
        }

        public ImageDescription GetImageDescription()
        {
            return new ImageDescription()
            {
                Width = Descriptor.Width,
                Height = Descriptor.Height,
                Depth = 1,
                Dimension = TextureDimension.Texture2D,
                Format = Format.R8G8B8A8_UNORM,
                ArraySize = 1,
                MipLevels = 1
            };
        }

        public FrameData GetFrameData(uint frameIndex)
        {
            var pixels = GetRawPixels(frameIndex);
            return new FrameData(pixels, GetImageDescription());
        }

        // Decoded pixels AND the palette-index stream both go: each is width*height*4 bytes per frame, so a 200-frame
        // 960x540 GIF sits on ~830 MB of them. The compressed data stays, so asking for a frame again simply decodes it
        // again - from frame 0, since a GIF frame is a delta over the one before it (see DecodeFrame).
        public void ReleaseDecodedFrames()
        {
            lock (_decodeSync)
            {
                foreach (var frame in frames)
                {
                    frame.RawPixels = null;
                    frame.IndexData = null;
                    frame.IsDecoded = false;
                }

                _decoding.Reset();
                _copying.Reset();
            }
        }
    }
}
