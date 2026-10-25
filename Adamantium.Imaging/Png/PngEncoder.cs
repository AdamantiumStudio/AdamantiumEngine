using System;
using System.IO;
using System.Linq;
using Adamantium.Imaging.Png.Chunks;
using Adamantium.Imaging.Png.IO;

namespace Adamantium.Imaging.Png
{
    internal class PngEncoder
    {
        private PngCompressor compressor;
        private Stream outputStream;
        private PNGStreamWriter pngStream;
        public PngEncoder(Stream outputStream)
        {
            compressor = new PngCompressor();
            this.outputStream = outputStream;
            pngStream = new PNGStreamWriter();
        }

        public PngEncoder()
        {
            compressor = new PngCompressor();
            pngStream = new PNGStreamWriter();
        }

        public uint Encode(PngImage pngImage, PngState state)
        {
            uint error = 0;

            /*check input values validity*/
            if ((state.InfoPng.ColorMode.ColorType == PngColorType.Palette || state.EncoderSettings.ForcePalette)
                && (state.InfoPng.ColorMode.PaletteSize == 0 || state.InfoPng.ColorMode.PaletteSize > 256))
            {
                /*invalid palette size, it is only allowed to be 1-256*/
                return 68;
            }

            if (state.EncoderSettings.BType > 2)
            {
                /*error: unexisting btype*/
                return 61;
            }

            state.Error = PngColorConversion.CheckColorValidity(state.InfoPng.ColorMode.ColorType, state.InfoPng.ColorMode.BitDepth);
            if (state.Error > 0)
            {
                return state.Error;
            }

            state.Error = PngColorConversion.CheckColorValidity(state.ColorModeRaw.ColorType, state.ColorModeRaw.BitDepth);
            if (state.Error > 0)
            {
                return state.Error;
            }

            // A PngImage that came out of the DECODER holds its frames still encoded - the pixels AND the frame's size
            // appear only when somebody asks for them. Everything below reads both, starting with the color-mode
            // choice, so ask first: encoding a picture straight after loading it (which is what transcoding does) was
            // otherwise deciding on a zero-sized image and throwing IndexOutOfRangeException further down.
            // ONLY when the pixels are genuinely missing: an image built from another format (a GIF, a JPEG) already
            // carries them, and asking it to decode would send it looking for compressed data it never had.
            for (uint i = 0; i < pngImage.Frames.Count; i++)
            {
                if (pngImage.Frames[(int)i].RawPixelBuffer is not { Length: > 0 }) pngImage.GetRawPixels(i);
            }

            /* color convert and compute scanline filter types */
            PngInfo info = new PngInfo(state.InfoPng);

            if (state.EncoderSettings.AutoConvert)
            {
                if (state.InfoPng.IsBackgroundDefined)
                {
                    var bgR = state.InfoPng.BackgroundR;
                    var bgG = state.InfoPng.BackgroundG;
                    var bgB = state.InfoPng.BackgroundB;
                    uint r = 0;
                    uint g = 0;
                    uint b = 0;

                    PngColorProfile profile = new PngColorProfile();
                    PngColorMode mode16 = PngColorMode.Create(PngColorType.RGB, 16);
                    PngColorConversion.ConvertRGB(ref r, ref g, ref b, bgR, bgG, bgB, mode16, state.InfoPng.ColorMode);
                    var frame = pngImage.Frames[0];
                    var (profileWidth, profileHeight) = SizeOf(frame);
                    PngColorConversion.GetColorProfile(profile, frame.RawPixelBuffer, profileWidth, profileHeight, state.ColorModeRaw);
                    profile.Add(r, g, b, ushort.MaxValue);
                    PngColorProfile.AutoChooseColorFromProfile(info.ColorMode, state.ColorModeRaw, profile);
                    error = PngColorConversion.ConvertRGB(ref info.BackgroundR, ref info.BackgroundG,
                        ref info.BackgroundB, bgR, bgG, bgB, info.ColorMode, state.InfoPng.ColorMode);
                    if (error > 0)
                    {
                        throw new PngEncoderException(error);
                    }
                }
                else
                {
                    var frame = pngImage.DefaultImage;
                    if (frame == null)
                    {
                        frame = pngImage.Frames[0];
                    }
                    // The frame's size, NOT its APNG sub-rectangle: fed the zeros a plain PNG carries there, the chooser
                    // inspects no pixels at all and leaves the color mode at gray/bit-depth-0 - which then sizes every
                    // buffer downstream to nothing and blows up inside the color conversion.
                    var (chooseWidth, chooseHeight) = SizeOf(frame);
                    PngColorProfile.AutoChooseColor(info.ColorMode, frame.RawPixelBuffer, chooseWidth, chooseHeight, state.ColorModeRaw);
                    //state.InfoRaw.ColorType = info.ColorMode.ColorType;
                    //state.InfoRaw.BitDepth = info.ColorMode.BitDepth;
                    state.ColorModeRaw = info.ColorMode;
                }
            }

            if (state.InfoPng.IsIccpDefined)
            {
                var grayICC = iCCP.IsGrayICCProfile(state.InfoPng.IccpProfile);
                var grayPng = info.ColorMode.ColorType == PngColorType.Gray || info.ColorMode.ColorType == PngColorType.GrayAlpha;
                /* TODO: perhaps instead of giving errors or less optimal compression, we can automatically modify
                the ICC profile here to say "GRAY" or "RGB " to match the PNG color type, unless this will require
                non trivial changes to the rest of the ICC profile */
                if (!grayICC && !iCCP.IsRGBICCProfile(state.InfoPng.IccpProfile))
                {
                    /* Disallowed profile color type for PNG */
                    throw new PngEncoderException(100);
                }
                if (!state.EncoderSettings.AutoConvert && grayICC != grayPng)
                {
                    /* Non recoverable: encoder not allowed to convert color type, and requested color type not
                    compatible with ICC color type */
                    throw new PngEncoderException(101);
                }
                if (grayICC && !grayPng)
                {
                    /* Non recoverable: trying to set grayscale ICC profile while colored pixels were given */
                    throw new PngEncoderException(102);
                    /* NOTE: this relies on the fact that PNGColorProfile.AutoChooseColor never returns palette for grayscale pixels */
                }
                if (!grayICC && grayPng)
                {
                    /* Recoverable but an unfortunate loss in compression density: We have grayscale pixels but
                    are forced to store them in more expensive RGB format that will repeat each value 3 times
                    because the PNG spec does not allow an RGB ICC profile with internal grayscale color data */
                    if (info.ColorMode.ColorType == PngColorType.Gray) info.ColorMode.ColorType = PngColorType.RGB;
                    if (info.ColorMode.ColorType == PngColorType.GrayAlpha) info.ColorMode.ColorType = PngColorType.RGBA;
                    if (info.ColorMode.BitDepth < 8) info.ColorMode.BitDepth = 8;
                }
            }

            if (state.InfoPng.ColorMode != info.ColorMode)
            {
                foreach (PngFrame frame in pngImage.Frames)
                {
                    var (frameWidth, frameHeight) = SizeOf(frame);
                    // PER SCANLINE, not for the image as a whole: PNG pads every row up to a byte boundary, so a
                    // sub-byte depth (a palette, gray) at a width that is not a multiple of 8/bpp needs one extra byte
                    // per row. Sizing it in one go came up short and the conversion wrote past the end of the buffer.
                    var bitsPerPixel = PngColorConversion.GetBitsPerPixel(info.ColorMode);
                    long size = (frameWidth * frameHeight * bitsPerPixel + 7) / 8;
                    var converted = new byte[size];
                    state.Error = PngColorConversion.Convert(converted, frame.RawPixelBuffer, info.ColorMode, state.InfoPng.ColorMode, (int)frameWidth, (int)frameHeight);
                    if (state.Error > 0)
                    {
                        throw new PngEncoderException(state.Error);
                    }
                    var compressedBuffer = new byte[0];
                    state.Error = PreprocessScanlines(ref compressedBuffer, converted, frameWidth, frameHeight, info, state.EncoderSettings);
                    frame.FrameData = compressedBuffer;
                    if (state.Error > 0)
                    {
                        throw new PngEncoderException(state.Error);
                    }
                }
            }
            else
            {
                foreach (PngFrame frame in pngImage.Frames)
                {
                    var (frameWidth, frameHeight) = SizeOf(frame);
                    var compressedBuffer = Array.Empty<byte>();
                    state.Error = PreprocessScanlines(ref compressedBuffer, frame.RawPixelBuffer, frameWidth, frameHeight, info, state.EncoderSettings);
                    frame.FrameData = compressedBuffer;
                    if (state.Error > 0)
                    {
                        throw new PngEncoderException(state.Error);
                    }
                }
            }
            state.InfoPng = info;
            var width = pngImage.Header.Width;
            var height = pngImage.Header.Height;
            pngStream.WriteSignature();
            pngStream.WriteIHDR(state, width, height);

            if (info.IsIccpDefined)
            {
                pngStream.WriteiCCP(state);
            }
            if (info.IsSrgbDefined)
            {
                pngStream.WritesRGB(state);
            }
            if (info.IsGamaDefined)
            {
                pngStream.WritegAMA(state);
            }
            if (info.IsChrmDefined)
            {
                pngStream.WritecHRM(state);
            }

            /*PLTE*/
            if (info.ColorMode.ColorType == PngColorType.Palette)
            {
                pngStream.WritePLTE(state);
            }
            if (state.EncoderSettings.ForcePalette 
                && (info.ColorMode.ColorType == PngColorType.RGB 
                || info.ColorMode.ColorType == PngColorType.RGBA))
            {
                pngStream.WritePLTE(state);
            }
            /*tRNS*/
            if ((info.ColorMode.ColorType == PngColorType.Gray ||
                info.ColorMode.ColorType == PngColorType.RGB)
                && info.ColorMode.IsKeyDefined)
            {
                pngStream.WritetRNS(state);
            }

            /*bKGD (must come between PLTE and the IDAt chunks*/
            if (info.IsBackgroundDefined)
            {
                pngStream.WritebKGD(state);
            }

            /*pHYs (must come before the IDAT chunks)*/
            if (info.IsPhysDefined)
            {
                pngStream.WritepHYs(state);
            }

            if (pngImage.IsMultiFrame)
            {
                pngStream.WriteacTL(state);
                if (pngImage.DefaultImage != null)
                {
                    pngStream.WriteIDAT(state, pngImage.DefaultImage.FrameData);
                }

                uint sequenceNumber = 0;
                for(int i = 0; i< pngImage.Frames.Count; ++i)
                {
                    var frame = pngImage.Frames[i];
                    if (pngImage.DefaultImage == null && i == 0)
                    {
                        pngStream.WritefcTL(frame);
                        pngStream.WriteIDAT(state, frame.FrameData);
                        ++sequenceNumber;
                        continue;
                    }

                    if (pngImage.DefaultImage!= null && frame.SequenceNumberFCTL == pngImage.DefaultImage.SequenceNumberFCTL)
                        continue;

                    frame.SequenceNumberFCTL = sequenceNumber;
                    ++sequenceNumber;
                    pngStream.WritefcTL(frame);
                    pngStream.WritefdAT(frame.FrameData, sequenceNumber, state);
                    sequenceNumber++;
                }
            }
            else
            {
                /*IDAT (multiple IDAT chunks must be consecutive)*/
                pngStream.WriteIDAT(state, pngImage.Frames[0].FrameData);
            }

            if (state.InfoPng.IsTimeDefined)
            {
                pngStream.WritetIME(state);
            }

            pngStream.WriteIEND(state);

            pngStream.Position = 0;
            pngStream.CopyTo(outputStream);

            return error;
        }

        public byte[] GetAllBytes()
        {
            pngStream.Position = 0;
            return pngStream.GetBuffer();
        }

        private unsafe void AddPaddingBits(byte[] outData, byte* inData, long olineBits, long ilineBits, uint height)
        {
            /*The opposite of the removePaddingBits function
            olinebits must be >= ilinebits*/
            var diff = olineBits - ilineBits;
            /*bit pointers*/
            int obp = 0;
            int ibp = 0;
            for (int y = 0; y != height; ++y)
            {
                for (int x = 0; x < ilineBits; ++x)
                {
                    byte bit = BitHelper.ReadBitFromReversedStream(ref ibp, inData);
                    BitHelper.SetBitOfReversedStream(ref obp, outData, bit);
                }
                /*obp += diff; --> no, fill in some value in the padding bits too, to avoid
                "Use of uninitialised value of size ###" warning from valgrind*/
                for (int x = 0; x != diff; ++x)
                {
                    BitHelper.SetBitOfReversedStream(ref obp, outData, 0);
                }
            }
        }

        /*
        This function converts the pure 2D image with the PNG's colortype, into filtered-padded-interlaced data. Steps:
        *) if no Adam7: 1) add padding bits (= possible extra bits per scanline if bpp < 8) 2) filter
        *) if adam7: 1) Adam7_interlace 2) 7x add padding bits 3) 7x filter
        */
        /*out must be buffer big enough to contain uncompressed IDAT chunk data, and in must contain the full image.
        return value is error**/
        /// <summary>
        /// The size to encode a frame at. <see cref="PngFrame.EncodedWidth"/> is the APNG SUB-FRAME rectangle and stays
        /// ZERO for an ordinary single-image PNG - the decoder uses that zero to mean "not an animation frame". Reading
        /// it blindly therefore produced a zero-sized buffer for every plain PNG, and encoding one threw
        /// IndexOutOfRangeException on the empty array; the whole-image size lives in <see cref="PngFrame.Width"/>.
        /// </summary>
        private static (uint Width, uint Height) SizeOf(PngFrame frame) =>
            frame.EncodedWidth != 0 && frame.EncodedHeight != 0
                ? (frame.EncodedWidth, frame.EncodedHeight)
                : (frame.Width, frame.Height);

        private unsafe uint PreprocessScanlines(ref byte[] outData, byte[] inData, uint width, uint height, PngInfo pngInfo, PngEncoderSettings settings)
        {
            uint error = 0;
            var bpp = PngColorConversion.GetBitsPerPixel(pngInfo.ColorMode);

            if (pngInfo.InterlaceMethod == InterlaceMethod.None)
            {
                /*image size plus an extra byte per scanline + possible padding bits*/
                var outSize = height + (height * ((width * bpp + 7) / 8));
                outData = new byte[outSize];

                /*non multiple of 8 bits per scanline, padding bits needed per scanline*/
                if (bpp < 8 && width * bpp != ((width * bpp + 7) / 8) * 8)
                {
                    byte[] padded = new byte[height * ((width * bpp + 7) / 8)];
                    fixed (byte* inPtr = &inData[0])
                    {
                        AddPaddingBits(padded, inPtr, ((width * bpp + 7) / 8) * 8, (width * bpp), height);
                    }
                    fixed (byte* paddedPtr = &padded[0])
                    {
                        fixed (byte* inPtr = &inData[0])
                        {
                            error = PNGFilter.Filter(paddedPtr, inPtr, width, height, pngInfo.ColorMode, settings);
                        }
                    }
                }
                else
                {
                    /*we can immediately filter into the out buffer, no other steps needed*/
                    fixed (byte* outPtr = &outData[0])
                    {
                        fixed (byte* inPtr = &inData[0])
                        {
                            error = PNGFilter.Filter(outPtr, inPtr, width, height, pngInfo.ColorMode, settings);
                        }
                    }
                }
            }
            else
            {
                uint[] passWidth = new uint[7];
                uint[] passHeight = new uint[7];
                uint[] filterPassStart = new uint[8];
                uint[] paddedPassStart = new uint[8];
                uint[] passStart = new uint[8];
                byte[] adam7;

                Adam7.GetPassValues(passWidth, passHeight, filterPassStart, paddedPassStart, passStart, width, height, bpp);

                var outSize = filterPassStart[7]; /*image size plus an extra byte per scanline + possible padding bits*/
                outData = new byte[outSize];

                adam7 = new byte[passStart[7]];

                Adam7.Interlace(adam7, inData, width, height, bpp);
                for (int i = 0; i != 7; ++i)
                {
                    if (bpp < 8)
                    {
                        byte[] padded = new byte[paddedPassStart[i + 1] - paddedPassStart[i]];

                        fixed (byte* adam7Ptr = &adam7[passStart[i]])
                        {
                            var olineBits = ((passWidth[i] * bpp + 7) / 8) * 8;
                            var ilineBits = passWidth[i] * bpp;
                            AddPaddingBits(padded, adam7Ptr, olineBits, ilineBits, passHeight[i]);
                        }

                        fixed (byte* outPtr = &outData[filterPassStart[i]])
                        {
                            fixed (byte* paddedPtr = &padded[0])
                            {
                                error = PNGFilter.Filter(outPtr, paddedPtr, width, height, pngInfo.ColorMode, settings);
                            }
                        }
                    }
                    else
                    {
                        fixed (byte* outPtr = &outData[filterPassStart[i]])
                        {
                            fixed (byte* paddedPtr = &adam7[paddedPassStart[i]])
                            {
                                error = PNGFilter.Filter(outPtr, paddedPtr, passWidth[i], passHeight[i], pngInfo.ColorMode, settings);
                            }
                        }
                    }

                    if (error > 0) break; 
                }
            }

            return error;
        }

        /*
        palette must have 4 * palettesize bytes allocated, and given in format RGBARGBARGBARGBA...
        returns 0 if the palette is opaque,
        returns 1 if the palette has a single color with alpha 0 ==> color key
        returns 2 if the palette is semi-translucent.
        */
        private PaletteTranslucency GetPaletteTranslucency(byte[] palette)
        {
            byte key = 0;
            /*the value of the color with alpha 0, so long as color keying is possible*/
            byte r = 0;
            byte g = 0;
            byte b = 0;

            for (int i = 0; i != palette.Length; ++i)
            {
                if (key == 0 && palette[4 * i + 3] == 0)
                {
                    r = palette[4 * i];
                    g = palette[4 * i + 1];
                    b = palette[4 * i + 2];
                    key = 1;
                    i = -1; /*restart from beginning, to detect earlier opaque colors with key's value*/
                }
                else if (palette[4 * i + 3] != 255)
                {
                    key = 2;
                    break;
                }
                /*when key, no opaque RGB may have key's RGB*/
                else if (key != 0 
                    && r == palette[i * 4]
                    && g == palette[i * 4 + 1]
                    && b == palette[i * 4 + 2])
                {
                    key = 2;
                    break;
                }
            }

            return (PaletteTranslucency)key;
        }

        enum PaletteTranslucency : byte
        {
            Opaque = 0,
            ColorKey = 1,
            SemiTranslucent = 2
        }
    }
}
