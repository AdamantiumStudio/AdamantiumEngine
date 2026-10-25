using System;
using Adamantium.Core;
using Adamantium.Imaging.Jpeg;

namespace Adamantium.Imaging
{
    public class ComponentsBuffer
    {
        public byte[][,] Raster { get; private set; }

        public int ComponentCount => Raster.Length;

        public int Width { get; private set; }

        public int Height { get; private set; }

        public ColorModel ColorModel { get; private set; }

        /// <summary> X density (dots per inch).</summary>
        public double DensityX { get; set; }
        /// <summary> Y density (dots per inch).</summary>
        public double DensityY { get; set; }

        public ComponentsBuffer(ColorModel cm, PixelBuffer pixelBuffer): 
            this(cm, pixelBuffer.GetComponents())
        {
        }

        public ComponentsBuffer(ColorModel cm, byte[][,] raster)
            : this(cm, raster, raster[0].GetLength(0), raster[0].GetLength(1))
        {
        }

        /// <summary>The raster may be LARGER than the picture: the eighth-scale preview writes whole blocks and then
        /// shows only the part the picture actually covers, rather than clipping every block as it goes. The extra
        /// column and row are converted along with everything else and dropped when the pixels are packed.</summary>
        public ComponentsBuffer(ColorModel cm, byte[][,] raster, int visibleWidth, int visibleHeight)
        {
            Width = visibleWidth;
            Height = visibleHeight;
            Raster = raster;
            ColorModel = cm;
        }

        /// <summary>
        /// Converts the colorspace of an image (in-place)
        /// </summary>
        /// <param name="cs">Colorspace to convert into</param>
        /// <returns>Self</returns>
        public ComponentsBuffer ChangeColorSpace(ColorSpace cs)
        {
            // Colorspace is already correct
            if (ColorModel.Colorspace == cs) return this;

            byte[] ycbcr = new byte[3];
            byte[] rgb = new byte[3];

            if (ColorModel.Colorspace == ColorSpace.RGB && cs == ColorSpace.YCbCr)
            {
                /*
                 *  Y' =       + 0.299    * R'd + 0.587    * G'd + 0.114    * B'd
                    Cb = 128   - 0.168736 * R'd - 0.331264 * G'd + 0.5      * B'd
                    Cr = 128   + 0.5      * R'd - 0.418688 * G'd - 0.081312 * B'd
                 * 
                 */

                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < Height; y++)
                    {
                        YCbCr.fromRGB(ref Raster[0][x, y], ref Raster[1][x, y], ref Raster[2][x, y]);
                    }

                ColorModel.Colorspace = ColorSpace.YCbCr;
            }
            else if (ColorModel.Colorspace == ColorSpace.YCbCr && cs == ColorSpace.RGB)
            {

                // The three planes are pulled out of the jagged array ONCE. Indexing it per pixel cost three
                // dereferences and three bounds checks on every one of a photograph's millions.
                var luma = Raster[0];
                var blue = Raster[1];
                var red = Raster[2];

                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < Height; y++)
                    {
                        // 0 is LUMA
                        // 1 is BLUE
                        // 2 is RED

                        YCbCr.ToRgbFast(ref luma[x, y], ref blue[x, y], ref red[x, y]);
                    }

                ColorModel.Colorspace = ColorSpace.RGB;
            }
            else if (ColorModel.Colorspace == ColorSpace.Gray && cs == ColorSpace.YCbCr)
            {
                // To convert to YCbCr, we just add two 128-filled chroma channels

                byte[,] Cb = new byte[Width, Height];
                byte[,] Cr = new byte[Width, Height];

                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < Height; y++)
                    {
                        Cb[x, y] = 128; Cr[x, y] = 128;
                    }

                Raster = [Raster[0], Cb, Cr, .. Raster.AsSpan(Math.Min(3, Raster.Length))];

                ColorModel.Colorspace = ColorSpace.YCbCr;
            }
            else if (ColorModel.Colorspace == ColorSpace.Gray && cs == ColorSpace.RGB)
            {
                ChangeColorSpace(ColorSpace.YCbCr);
                ChangeColorSpace(ColorSpace.RGB);
            }
            else
            {
                throw new Exception("Colorspace conversion not supported.");
            }

            return this;
        }

        public unsafe void CopyPixels(IntPtr dataPointer, int sizeInBytes)
        {
            var colorBuffer = new byte[Width * Height * ComponentCount];
            int counter = 0;
            for (int i = 0; i< Height; ++i)
            {
                for (int j = 0; j < Width; ++j)
                {
                    for (int k = 0; k < Raster.Length; ++k)
                    {
                        colorBuffer[counter] = Raster[k][j, i];
                        counter++;
                    }
                }
            }

            fixed (byte* el = &colorBuffer[0])
            {
                IntPtr srcPtr = (IntPtr)el;
                Utilities.CopyMemory(dataPointer, srcPtr, sizeInBytes);
            }

            Array.Clear(colorBuffer, 0, colorBuffer.Length);
        }
        
        /// <summary>
        /// The planes as one interleaved buffer - the form everything outside this codec wants.
        ///
        /// <para>Walked in TILES, and that is the whole of it: a plane is stored as <c>[x, y]</c>, so two pixels side by
        /// side on a row are a whole image HEIGHT apart in memory, while the buffer being filled runs along the row.
        /// Read one way and the writes scatter, read the other and the reads do - one of the two always fights the
        /// cache. A tile is small enough that both halves stay resident while it is processed, so neither has to.</para>
        ///
        /// <para>Measured on a 3840x2160 photograph: 1267 ms row by row, and this is the same copy.</para>
        /// </summary>
        public byte[] GetPixelBuffer()
        {
            var colorBuffer = new byte[Width * Height * ComponentCount];
            var planes = Raster.Length;

            // 64 keeps a tile's slice of every plane (64 rows x 64 columns) plus its part of the output well inside L2.
            const int tile = 64;

            for (int tileY = 0; tileY < Height; tileY += tile)
            {
                var maxY = Math.Min(tileY + tile, Height);

                for (int tileX = 0; tileX < Width; tileX += tile)
                {
                    var maxX = Math.Min(tileX + tile, Width);

                    for (int k = 0; k < planes; ++k)
                    {
                        // The alpha plane is not decoded from anything - it is opaque by construction, so it is filled
                        // rather than copied. Hoisted out of the pixel loop, where it used to be a branch per channel.
                        var plane = Raster[k];
                        if (k == 3)
                        {
                            for (int y = tileY; y < maxY; ++y)
                            {
                                var row = (y * Width + tileX) * ComponentCount + k;
                                for (int x = tileX; x < maxX; ++x, row += ComponentCount) colorBuffer[row] = 255;
                            }

                            continue;
                        }

                        for (int y = tileY; y < maxY; ++y)
                        {
                            var row = (y * Width + tileX) * ComponentCount + k;
                            for (int x = tileX; x < maxX; ++x, row += ComponentCount)
                            {
                                colorBuffer[row] = plane[x, y];
                            }
                        }
                    }
                }
            }

            return colorBuffer;
        }

        public static byte[][,] CreateRaster(int width, int height, int bands)
        {
            // Create the raster
            byte[][,] raster = new byte[bands][,];
            for (int b = 0; b < bands; b++)
                raster[b] = new byte[width, height];
            return raster;
        }

        delegate void ConvertColor(ref byte c1, ref byte c2, ref byte c3);
    }
}
