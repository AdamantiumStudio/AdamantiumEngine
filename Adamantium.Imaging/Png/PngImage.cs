using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Imaging.Png.Chunks;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Png
{
    internal class PngImage : IRawBitmap
    {
        private readonly PngCompressor compressor;
        // Frames decode lazily and may be pulled from the async loader AND the render/animation thread at once. Serialize
        // it: ProcessFrame mutates shared PngFrame state (it zeroes XOffset/YOffset after compositing), so two concurrent
        // decodes of the same frame raced and the second composited the sub-region at (0,0) - the frame distortion.
        private readonly object _frameDecodeLock = new object();

        public PngImage()
        {
            Frames = new List<PngFrame>();
            compressor = new PngCompressor();
        }
        
        public PngState State { get; set; }

        public bool HasMipLevels => false;
        public uint MipLevelsCount => 0;
        public uint NumberOfReplays => RepeatCount;

        public uint FramesCount => (uint)Frames.Count;
        
        public byte[] GetRawPixels(uint frameIndex)
        {
            var frame = frameIndex < FramesCount ? Frames[(int)frameIndex] : Frames.Last();
            if (frameIndex >= FramesCount)
            {
                frameIndex = FramesCount - 1;
            }
            return DecodeFrame(frame, frameIndex);
        }

        public FrameData GetMipLevelData(uint mipLevel)
        {
            return GetFrameData(0);
        }

        public ImageDescription GetImageDescription()
        {
            return new ImageDescription()
            {
                Width = Width,
                Height = Height,
                Depth = 1,
                Dimension = TextureDimension.Texture2D,
                Format = GetFormat(),
                ArraySize = 1,
                MipLevels = 1
            };
        }

        public FrameData GetFrameData(uint frameIndex)
        {
            return new FrameData(GetRawPixels(frameIndex), GetImageDescription());
        }

        public uint RepeatCount { get; set; }

        public IHDR Header { get; set; }
        
        public PngColorType ColorType { get; set; }

        public List<PngFrame> Frames { get; }

        public PngFrame DefaultImage { get; set; }

        public uint Width
        {
            get => (uint)Header.Width;
            set => Header.Width = (int)value;
        }
        public uint Height
        {
            get => (uint)Header.Height;
            set => Header.Height = (int)value;
        }

        public SurfaceFormat PixelFormat => GetFormat();
        public bool IsMultiFrame => Frames.Count > 1;

        private SurfaceFormat GetFormat()
        {
            var mode = State?.ColorModeRaw;
            var channels = PngColorConversion.GetNumberOfColorChannels(mode?.ColorType ?? ColorType);
            switch (channels * (mode?.BitDepth ?? (uint)Header.BitDepth))
            {
                case 8:
                    return Format.R8_UNORM;
                case 24:
                    return Format.R8G8B8_UNORM;
                case 32: return Format.R8G8B8A8_UNORM;
            }

            return SurfaceFormat.Undefined;
        }

        // The decoded pixels of every frame, dropped: an APNG handed to the GPU as one texture has no reader left for
        // them. The encoded data stays, so a later request decodes again (see DecodeFrame).
        public void ReleaseDecodedFrames()
        {
            lock (_frameDecodeLock)
            {
                foreach (var frame in Frames)
                {
                    frame.RawPixelBuffer = null;
                    frame.IsDecoded = false;
                }
            }
        }

        private byte[] DecodeFrame(PngFrame frame, uint index)
        {
            if (frame.IsDecoded) return frame.RawPixelBuffer;
            lock (_frameDecodeLock)
            {
                if (frame.IsDecoded) return frame.RawPixelBuffer;   // another thread finished it while we waited
                return DecodeFrameCore(frame, index);
            }
        }

        private byte[] DecodeFrameCore(PngFrame frame, uint index)
        {
            long predict = 0;
            int width = frame.EncodedWidth != 0 ? (int)frame.EncodedWidth : Header.Width;
            int height = frame.EncodedHeight != 0 ? (int)frame.EncodedHeight : Header.Height;
            predict = 0;
            if (State.InfoPng.InterlaceMethod == InterlaceMethod.None)
            {
                predict = PngDecoder.GetRawSizeIdat(width, height, State.InfoPng.ColorMode);
            }
            else
            {
                /*Adam-7 interlaced: predicted size is the sum of the 7 sub-images sizes*/
                var colorMode = State.InfoPng.ColorMode;
                predict += PngDecoder.GetRawSizeIdat((width + 7) >> 3, (height + 7) >> 3, colorMode);
                if (width > 4)
                {
                    predict += PngDecoder.GetRawSizeIdat((width + 3) >> 3, (height + 7) >> 3, colorMode);
                }

                predict += PngDecoder.GetRawSizeIdat((width + 3) >> 2, (height + 3) >> 3, colorMode);
                if (width > 2)
                {
                    predict += PngDecoder.GetRawSizeIdat((width + 1) >> 2, (height + 3) >> 2, colorMode);
                }

                predict += PngDecoder.GetRawSizeIdat((width + 1) >> 1, (height + 1) >> 2, colorMode);
                if (width > 1)
                {
                    predict += PngDecoder.GetRawSizeIdat(width >> 1, (height + 1) >> 1, colorMode);
                }

                predict += PngDecoder.GetRawSizeIdat(width, height >> 1, colorMode);
            }
            
            var scanlines = new byte[predict];

            State.Error = compressor.Decompress(frame.FrameData, State.DecoderSettings, ref scanlines, out var scanlinesLength);

            // Short data would unfilter whatever lies past it.
            if (State.Error == 0 && scanlinesLength < predict)
            {
                State.Error = 91;
            }

            // The pixels, not the IDAT size with its filter byte per line: an image needing no conversion is handed out as is.
            long bufferSize = PngDecoder.GetRawSizeLct(width, height, State.InfoPng.ColorMode);
            frame.RawPixelBuffer = new byte[bufferSize];

            if (State.Error == 0)
            {
                State.Error = PostProcessScanline(frame.RawPixelBuffer, scanlines, width, height, State.InfoPng);
            }

            if (State.Error > 0)
            {
                throw new PngDecodeException(State.Error);
            }

            ProcessFrame(frame, index);

            return frame.RawPixelBuffer;
        }
        
        /// <summary>
        /// This function converts the filtered-padded-interlaced data into pure 2D image buffer with the PNG's colortype.
        /// Steps:
        /// *) if no Adam7: 1) unfilter 2) remove padding bits (= posible extra bits per scanline if bpp < 8)
        /// *) if adam7: 1) 7x unfilter 2) 7x remove padding bits 3) Adam7_deinterlace
        /// NOTE: the in buffer will be overwritten with intermediate data!
        /// </summary>
        /// <returns>Error code</returns>
        internal unsafe uint PostProcessScanline(byte[] rawBuffer, byte[] inputData, int width, int height, PngInfo infoPng)
        {
            uint error = 0;
            var bpp = PngColorConversion.GetBitsPerPixel(infoPng.ColorMode);
            if (bpp == 0)
            {
                /*error: invalid colortype*/
                return 31;
            }

            if (infoPng.InterlaceMethod == 0)
            {
                fixed (byte* inPtr = &inputData[0])
                {
                    fixed (byte* rawPtr = &rawBuffer[0])
                    {
                        if (bpp < 8 && width * bpp != ((width * bpp + 7) / 8) * 8)
                        {
                            error = PNGFilter.Unfilter(inPtr, inPtr, width, height, (int)bpp);
                            if (error > 0)
                            {
                                return error;
                            }
                            RemovePaddingBits(rawPtr, inPtr, (uint)(width * bpp), (uint)((width * bpp + 7) / 8) * 8, (uint)height);
                        }
                        else
                        {
                            error = PNGFilter.Unfilter(rawPtr, inPtr, width, height, (int)bpp);
                        }
                    }
                }
            }
            else /*interlace_method is 1 (Adam7)*/
            {
                uint[] passWidth = new uint[7];
                uint[] passHeight = new uint[7];
                uint[] filterPassStart = new uint[8];
                uint[] paddedPassStart = new uint[8];
                uint[] passStart = new uint[8];

                Adam7.GetPassValues(passWidth, passHeight, filterPassStart, paddedPassStart, passStart, (uint)width, (uint)height, bpp);

                for (int i = 0; i != 7; ++i)
                {
                    fixed (byte* rawPtr = &inputData[paddedPassStart[i]])
                    {
                        fixed (byte* inPtr = &inputData[filterPassStart[i]])
                        {
                            error = PNGFilter.Unfilter(rawPtr, inPtr, (int)passWidth[i], (int)passHeight[i], (int)bpp);
                        }
                    }

                    /*TODO: possible efficiency improvement: if in this reduced image the bits fit nicely in 1 scanline,
                    move bytes instead of bits or move not at all*/
                    if (bpp < 8)
                    {
                        /*remove padding bits in scanlines; after this there still may be padding
                        bits between the different reduced images: each reduced image still starts nicely at a byte*/
                        fixed (byte* rawPtr = &inputData[passStart[i]])
                        {
                            fixed (byte* inPtr = &inputData[paddedPassStart[i]])
                            {
                                RemovePaddingBits(rawPtr, inPtr, passWidth[i] * bpp, 
                                    ((passWidth[i] * bpp + 7) / 8) * 8, passHeight[i]);
                            }
                        }
                    }
                }

                Adam7.Deinterlace(rawBuffer, inputData, (uint)width, (uint)height, bpp);
            }

            return error;
        }
        
        private unsafe void RemovePaddingBits(byte* rawBuffer, byte* inputData, uint olinebits, uint ilinebits, uint height)
        {
            /*
            After filtering there are still padding bits if scanlines have non multiple of 8 bit amounts. They need
            to be removed (except at last scanline of (Adam7-reduced) image) before working with pure image buffers
            for the Adam7 code, the color convert code and the output to the user.
            in and out are allowed to be the same buffer, in may also be higher but still overlapping; in must
            have >= ilinebits*h bits, out must have >= olinebits*h bits, olinebits must be <= ilinebits
            also used to move bits after earlier such operations happened, e.g. in a sequence of reduced images from Adam7
            only useful if (ilinebits - olinebits) is a value in the range 1..7
            */
            uint diff = ilinebits - olinebits;
            /*input and output bit pointers*/
            int ibp = 0;
            int obp = 0;
            for (int i = 0; i < height; ++i)
            {
                for (int x = 0; x < olinebits; ++x)
                {
                    byte bit = BitHelper.ReadBitFromReversedStream(ref ibp, inputData);
                    BitHelper.SetBitOfReversedStream(ref obp, rawBuffer, bit);
                }

                ibp += (int)diff;
            }
        }

        private void ProcessFrame(PngFrame frame, uint index)
        {
            frame.Width = (uint)Header.Width;
            frame.Height = (uint)Header.Height;
            ConvertColorsIfNeeded(frame, State);

            if (frame.EncodedWidth == 0 || frame.EncodedHeight == 0)
            {
                // A still image is decoded too: without this every GetRawPixels decoded it again.
                frame.IsDecoded = true;
                return;
            }

            var bytesPerPixel = (int)(PngColorConversion.GetBitsPerPixel(State.ColorModeRaw) / 8);
            int lineLength = Header.Width * bytesPerPixel;
            int canvasSize = lineLength * Header.Height;

            // APNG frames are sub-regions composited onto a full-size canvas. The buffer MUST be the full
            // canvas (PixelWidth*PixelHeight*bpp), not the sub-region: a sub-region-sized buffer overruns the
            // texture upload (vkCmdCopyBufferToImage reads past the staging buffer) and loses the device.
            // The canvas the current frame draws onto is what the PREVIOUS frame left after its DisposeOp (APNG spec):
            //  * None       -> keep the previous frame's output as-is;
            //  * Background  -> keep it, but clear the previous frame's own region to transparent black;
            //  * Previous    -> discard the previous frame and restore the canvas from BEFORE it.
            // The old code only handled None (all other ops fell through to a black canvas), so any Background/Previous
            // frame showed just its sub-region on black - the "distortion" the user saw on certain frames.
            byte[] pixels = new byte[canvasSize];
            if (index > 0)
            {
                var previous = Frames[(int)index - 1];
                if (previous.DisposeOp == DisposeOp.Previous)
                {
                    if (previous.CanvasBeforeFrame != null)
                        Array.Copy(previous.CanvasBeforeFrame, pixels, Math.Min(previous.CanvasBeforeFrame.Length, canvasSize));
                }
                else if (previous.RawPixelBuffer != null)
                {
                    Array.Copy(previous.RawPixelBuffer, pixels, Math.Min(previous.RawPixelBuffer.Length, canvasSize));
                    if (previous.DisposeOp == DisposeOp.Background)
                        ClearRegion(pixels, previous.RegionX, previous.RegionY, previous.RegionWidth, previous.RegionHeight, lineLength, bytesPerPixel);
                }
            }

            // Record this frame's region (XOffset/YOffset are zeroed right after compositing) and, for a Previous-dispose
            // frame, snapshot the canvas as it is NOW - before this frame draws - so the next frame can revert to it.
            frame.RegionX = frame.XOffset;
            frame.RegionY = frame.YOffset;
            frame.RegionWidth = frame.EncodedWidth != 0 ? frame.EncodedWidth : (uint)Header.Width;
            frame.RegionHeight = frame.EncodedHeight != 0 ? frame.EncodedHeight : (uint)Header.Height;
            if (frame.DisposeOp == DisposeOp.Previous)
                frame.CanvasBeforeFrame = (byte[])pixels.Clone();

            var src = frame.RawPixelBuffer;
            int rowBytes = (int)frame.EncodedWidth * bytesPerPixel;
            for (int k = 0; k < frame.EncodedHeight; ++k)
            {
                int dstIndex = (((int)frame.YOffset + k) * lineLength) + ((int)frame.XOffset * bytesPerPixel);
                int srcIndex = k * rowBytes;
                if (dstIndex < 0 || dstIndex + rowBytes > canvasSize || srcIndex + rowBytes > src.Length)
                {
                    continue;
                }

                if (frame.BlendOp == BlendOp.Over && bytesPerPixel == 4)
                {
                    // straight-alpha source-over: out = src*srcA + dst*(1-srcA)
                    for (int x = 0; x < rowBytes; x += 4)
                    {
                        int s = srcIndex + x;
                        int d = dstIndex + x;
                        float srcA = src[s + 3] / 255.0f;
                        float invA = 1.0f - srcA;
                        pixels[d + 0] = (byte)(src[s + 0] * srcA + pixels[d + 0] * invA);
                        pixels[d + 1] = (byte)(src[s + 1] * srcA + pixels[d + 1] * invA);
                        pixels[d + 2] = (byte)(src[s + 2] * srcA + pixels[d + 2] * invA);
                        pixels[d + 3] = (byte)(src[s + 3] + pixels[d + 3] * invA);
                    }
                }
                else // BlendOp.Source: replace the region as-is
                {
                    Array.Copy(src, srcIndex, pixels, dstIndex, rowBytes);
                }
            }

            frame.XOffset = 0;
            frame.YOffset = 0;
            frame.RawPixelBuffer = pixels;
            frame.IsDecoded = true;
        }

        // Clears a sub-region of the canvas to transparent black (APNG DisposeOp.Background), row by row so the region's
        // stride is the sub-region width while the destination advances by the full canvas line.
        private static void ClearRegion(byte[] pixels, uint x, uint y, uint width, uint height, int lineLength, int bytesPerPixel)
        {
            if (width == 0 || height == 0) return;
            int rowBytes = (int)width * bytesPerPixel;
            for (int k = 0; k < height; ++k)
            {
                int dstIndex = (((int)y + k) * lineLength) + ((int)x * bytesPerPixel);
                if (dstIndex < 0 || dstIndex + rowBytes > pixels.Length) continue;
                Array.Clear(pixels, dstIndex, rowBytes);
            }
        }

        private void ConvertColorsIfNeeded(PngFrame frame, PngState state)
        {
            if (!state.DecoderSettings.ColorConvert || state.ColorModeRaw == state.InfoPng.ColorMode)
            {
                /*same color type, no copying or converting of data needed*/
                /*store the info_png color settings on the info_raw so that the info_raw still reflects what colortype
                the raw image has to the end user*/
                if (!state.DecoderSettings.ColorConvert)
                {
                    state.ColorModeRaw = state.InfoPng.ColorMode;
                }
            }
            else
            {
                /*color conversion needed; sort of copy of the data*/
                if (!(state.ColorModeRaw.ColorType is PngColorType.RGB or PngColorType.RGBA)
                    && state.ColorModeRaw.BitDepth != 8)
                {
                    /*unsupported color mode conversion*/
                    throw new PngDecodeException(56);
                }

                var width = (int)frame.EncodedWidth;
                var height = (int)frame.EncodedHeight;

                if (width == 0 || height == 0)
                {
                    width = Header.Width;
                    height = Header.Height;
                }

                int rawBufferSize = (int)PngDecoder.GetRawSizeLct(width, height, state.ColorModeRaw);
                var outBuffer = new byte[rawBufferSize];

                state.Error = PngColorConversion.Convert(outBuffer, frame.RawPixelBuffer, state.ColorModeRaw, state.InfoPng.ColorMode, width, height);
                frame.RawPixelBuffer = outBuffer;
                if (state.Error > 0)
                {
                    throw new PngDecodeException(state.Error);
                }
            }
        }

        public static PngImage FromImage(IRawBitmap image)
        {
            var png = FromPixelBuffers(image);
            png.Frames.Add(GetFrameFromBuffer(image.GetFrameData(0), 0));
            png.Header = new IHDR();
            png.Header.Width = (int)image.Width;
            png.Header.Height = (int)image.Height;

            return png;
        }

        public static PngImage FromPixelBuffers(IRawBitmap image)
        {
            var pngImage = new PngImage();
            for (uint i = 0; i < image.FramesCount; ++i)
            {
                var frame = GetFrameFromBuffer(image.GetFrameData(i), i);
                pngImage.Frames.Add(frame);
            }

            return pngImage;
        }

        private static PngFrame GetFrameFromBuffer(FrameData frameData, uint sequenceNumber)
        {
            if (frameData == null)
            {
                return null;
            }

            var pixels = frameData.RawPixels;
            var frame = new PngFrame(pixels, frameData.Description.Width, frameData.Description.Height, frameData.Description.Format.SizeOfInBytes());
            frame.DelayNumerator = 1;
            frame.DelayDenominator = 16;
            frame.XOffset = 0;
            frame.YOffset = 0;
            frame.SequenceNumberFCTL = sequenceNumber;

            return frame;
        }
    }
}
