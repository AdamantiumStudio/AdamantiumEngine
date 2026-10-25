namespace Adamantium.Imaging.Png
{
    public static class Adam7
    {
        static uint[] Adam7_IX = { 0, 4, 0, 2, 0, 1, 0 }; /*x start values*/
        static uint[] Adam7_IY = { 0, 0, 4, 0, 2, 0, 1 }; /*y start values*/
        static uint[] Adam7_DX = { 8, 8, 4, 4, 2, 2, 1 }; /*x delta values*/
        static uint[] Adam7_DY = { 8, 8, 8, 4, 4, 2, 2 }; /*y delta values*/

        /*
        Outputs various dimensions and positions in the image related to the Adam7 reduced images.
        passWidth: output containing the width of the 7 passes
        passh: output containing the height of the 7 passes
        filter_passstart: output containing the index of the start and end of each
         reduced image with filter bytes
        padded_passstart output containing the index of the start and end of each
         reduced image when without filter bytes but with padded scanlines
        passstart: output containing the index of the start and end of each reduced
         image without padding between scanlines, but still padding between the images
        w, h: width and height of non-interlaced image
        bpp: bits per pixel
        "padded" is only relevant if bpp is less than 8 and a scanline or image does not
         end at a full byte
        */
        public static void GetPassValues(uint[] passWidth, uint[] passHeight, uint[] filterPassStart,
            uint[] paddedPassStart, uint[] passStart, uint width, uint height, uint bpp)
        {
            for (int i = 0; i != 7; ++i)
            {
                passWidth[i] = (width + Adam7_DX[i] - Adam7_IX[i] - 1) / Adam7_DX[i];
                passHeight[i] = (height + Adam7_DY[i] - Adam7_IY[i] - 1) / Adam7_DY[i];
                if (passWidth[i] == 0) passHeight[i] = 0;
                if (passHeight[i] == 0) passWidth[i] = 0;
            }

            filterPassStart[0] = paddedPassStart[0] = passStart[0] = 0;
            for (int  i = 0; i != 7; ++i)
            {
                /*if passWidth[i] is 0, it's 0 bytes, not 1 (no filtertype-byte)*/
                filterPassStart[i + 1] = filterPassStart[i] +
                    ((passWidth[i] > 0 && passHeight[i] > 0 ? passHeight[i] * (1 + (passWidth[i] * bpp + 7) / 8) : 0));
                /*bits padded if needed to fill full byte at end of each scanline*/
                paddedPassStart[i + 1] = paddedPassStart[i] + passHeight[i] * ((passWidth[i] * bpp + 7) / 8);
                /*only padded at end of reduced image*/
                passStart[i + 1] = passStart[i] + (passHeight[i] * passWidth[i] * bpp + 7) / 8;
            }
        }

        public static unsafe void Deinterlace(byte[] outBuffer, byte[] inputData, uint width, uint height, uint bpp)
        {
            uint[] passw = new uint[7];
            uint[] passh = new uint[7];
            uint[] filterPassStart = new uint[8];
            uint[] paddedPassStart = new uint[8];
            uint[] passStart = new uint[8];

            GetPassValues(passw, passh, filterPassStart, paddedPassStart, passStart, width, height, bpp);

            if (bpp >= 8)
            {
                for (int i = 0; i != 7; ++i)
                {
                    uint x, y, b;
                    uint byteWidth = bpp / 8;
                    for (y = 0; y < passh[i]; ++y)
                    {
                        for (x = 0; x< passw[i]; ++x)
                        {
                            var pixelInStart = passStart[i] + (y * passw[i] + x) * byteWidth;
                            var pixelOutStart = ((Adam7_IY[i] + y * Adam7_DY[i]) * width + Adam7_IX[i] + x * Adam7_DX[i]) * byteWidth;
                            for (b = 0; b < byteWidth; ++b)
                            {
                                outBuffer[pixelOutStart + b] = inputData[pixelInStart + b];
                            }
                        }
                    }
                }
            }
            else /*bpp < 8: Adam7 with pixels < 8 bit is a bit trickier: with bit pointers*/
            {
                for (int i = 0; i != 7; ++i)
                {
                    uint x, y, b;
                    uint ilinebits = bpp * passw[i];
                    uint olinebits = bpp * width;
                    int obp, ibp; /*bit pointers (for out and in buffer)*/
                    for (y = 0; y < passh[i]; ++y)
                        for (x = 0; x < passw[i]; ++x)
                        {
                            ibp = (int)((8 * passStart[i]) + (y * ilinebits + x * bpp));
                            obp = (int)((Adam7_IY[i] + y * Adam7_DY[i]) * olinebits + (Adam7_IX[i] + x * Adam7_DX[i]) * bpp);

                            fixed (byte* inputPtr = &inputData[0])
                            {
                                fixed (byte* outPtr = &outBuffer[0])
                                {
                                    for (b = 0; b < bpp; ++b)
                                    {
                                        byte bit = BitHelper.ReadBitFromReversedStream(ref ibp, inputPtr);
                                        /*note that this function assumes the out buffer is completely 0, use setBitOfReversedStream otherwise*/
                                        BitHelper.SetBitOfReversedStream0(ref obp, outPtr, bit);
                                    }
                                }
                            }
                        }
                }
            }
        }


        /*
        in: non-interlaced image with size w*h
        out: the same pixels, but re-ordered according to PNG's Adam7 interlacing, with
         no padding bits between scanlines, but between reduced images so that each
         reduced image starts at a byte.
        bpp: bits per pixel
        there are no padding bits, not between scanlines, not between reduced images
        in has the following size in bits: w * h * bpp.
        out is possibly bigger due to padding bits between reduced images
        NOTE: comments about padding bits are only relevant if bpp < 8
        */
        public static void Interlace(byte[] outBuffer, byte[] inputData, uint width, uint height, uint bpp)
        {
            uint[] passw = new uint[7];
            uint[] passh = new uint[7];
            uint[] filterPassStart = new uint[8];
            uint[] paddedPassStart = new uint[8];
            uint[] passStart = new uint[8];

            GetPassValues(passw, passh, filterPassStart, paddedPassStart, passStart, width, height, bpp);

            if (bpp >= 8)
            {
                for (int i = 0; i != 7; ++i)
                {
                    var byteWidth = bpp / 8;
                    for (int y = 0; y < passh[i]; ++y)
                    {
                        for (int x = 0; x < passw[i]; ++x)
                        {
                            var pixelInStart = ((Adam7_IY[i] + y * Adam7_DY[i]) * width + Adam7_IX[i] + x * Adam7_DX[i]) * byteWidth;
                            var pixelOutStart = passStart[i] + (y * passw[i] + x) * byteWidth;
                            for (int b = 0; b < byteWidth; ++b)
                            {
                                outBuffer[pixelOutStart + b] = inputData[pixelInStart + b];
                            }
                        }
                    }
                }
            }
            else /*bpp < 8: Adam7 with pixels < 8 bit is a bit trickier: with bit pointers*/
            {
                for (int i = 0; i != 7; ++i)
                {
                    var ilinebits = bpp * passw[i];
                    var olinebits = bpp * width;
                    int obp, ibp; /*bit pointers (for out and in buffer)*/
                    for (int y = 0; y< passh[i]; ++y)
                    {
                        for (int x = 0; x < passw[i]; ++x)
                        {
                            ibp = (int)((Adam7_IY[i] + y * Adam7_DY[i]) * olinebits + (Adam7_IX[i] + x * Adam7_DX[i]) * bpp);
                            obp = (int)((8 * passStart[i]) + (y * ilinebits + x * bpp));
                            for (int b = 0; b < bpp; ++b)
                            {
                                byte bit = BitHelper.ReadBitFromReversedStream(ref ibp, inputData);
                                BitHelper.SetBitOfReversedStream(ref obp, outBuffer, bit);
                            }
                        }
                    }
                }
            }
        }
    }
}
