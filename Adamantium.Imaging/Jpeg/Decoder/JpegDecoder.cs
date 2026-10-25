/// Copyright (c) 2008 Jeffrey Powers for Fluxcapacity Open Source.
/// Under the MIT License, details: License.txt.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Imaging.Jpeg.IO;

namespace Adamantium.Imaging.Jpeg.Decoder
{
    public class JpegDecoder
    {
        public BlockUpsamplingMode BlockUpsamplingMode { get; set; }

        /// <summary>Whether three components are red, green and blue as they are - as TIFF stores JPEG of RGB data -
        /// rather than YCbCr to convert.</summary>
        public bool ComponentsAreRgb { get; set; }

        byte majorVersion, minorVersion;
        private enum UnitType { None = 0, Inches = 1, Centimeters = 2 };
        UnitType Units;
        ushort XDensity, YDensity;
        byte Xthumbnail, Ythumbnail;
        byte[] thumbnail;

        bool progressive = false;

        byte marker;

        /// <summary>
        /// This decoder expects JFIF 1.02 encoding.
        /// </summary>
        internal const byte MAJOR_VERSION = 1;
        internal const byte MINOR_VERSION = 2;

        /// <summary>
        /// The length of the JFIF field not including thumbnail data.
        /// </summary>
        internal static short JFIF_FIXED_LENGTH = 16;

        /// <summary>
        /// The length of the JFIF extension field not including extension data.
        /// </summary>
        internal static short JFXX_FIXED_LENGTH = 8;

        private JPEGBinaryReader jpegReader;

        List<JpegFrame> jpegFrames = new List<JpegFrame>();

        JpegHuffmanTable[] dcTables = new JpegHuffmanTable[4];
        JpegHuffmanTable[] acTables = new JpegHuffmanTable[4];
        JpegQuantizationTable[] qTables = new JpegQuantizationTable[4];

        public JpegDecoder(Stream input)
        {
            jpegReader = new JPEGBinaryReader(input);

            if (jpegReader.GetNextMarker() != JPEGMarker.SOI)
                throw new Exception("Failed to find SOI marker.");
        }

        /// <summary>
        /// Tries to parse the JFIF APP0 header
        /// See http://en.wikipedia.org/wiki/JFIF
        /// </summary>
        private bool TryParseJFIF(byte[] data)
        {
            IO.BinaryReader reader = new IO.BinaryReader(new MemoryStream(data));

            int length = data.Length + 2; // Data & length

            if (!(length >= JFIF_FIXED_LENGTH))
                return false;  // Header's too small.

            byte[] identifier = new byte[5];
            reader.Read(identifier, 0, identifier.Length);
            if (identifier[0] != JPEGMarker.JFIF_J
                || identifier[1] != JPEGMarker.JFIF_F
                || identifier[2] != JPEGMarker.JFIF_I
                || identifier[3] != JPEGMarker.JFIF_F
                || identifier[4] != JPEGMarker.X00)
                return false;  // Incorrect bytes

            majorVersion = reader.ReadByte();
            minorVersion = reader.ReadByte();
            if (majorVersion != MAJOR_VERSION
                || majorVersion == MAJOR_VERSION
                    && minorVersion > MINOR_VERSION) // changed from <
                return false; // Unsupported version

            Units = (UnitType)reader.ReadByte();
            if (Units != UnitType.None &&
                Units != UnitType.Inches &&
                Units != UnitType.Centimeters)
                return false; // Invalid units

            XDensity = reader.ReadShort();
            YDensity = reader.ReadShort();
            Xthumbnail = reader.ReadByte();
            Ythumbnail = reader.ReadByte();

            // 3 * for RGB data
            int thumbnailLength = 3 * Xthumbnail * Ythumbnail;
            if (length > JFIF_FIXED_LENGTH
                && thumbnailLength != length - JFIF_FIXED_LENGTH)
                return false; // Thumbnail fields invalid

            if (thumbnailLength > 0)
            {
                thumbnail = new byte[thumbnailLength];
                if (reader.Read(thumbnail, 0, thumbnailLength) != thumbnailLength)
                    return false; // Thumbnail data was missing!

            }

            return true;
        }

        // 1 for the full picture, 8 for the eighth-scale preview. Read where the raster is sized and where the blocks
        // are turned into pixels - nothing else in the decode cares.
        private int _scaleDivisor = 1;

        /// <summary>
        /// The picture at an EIGHTH of its size, for a fraction of the work - a thumbnail, a preview, or a backdrop
        /// that is going to be blurred past recognition anyway.
        ///
        /// <para>Not a decode followed by a resize. Every 8x8 block of a JPEG carries its average value as one
        /// coefficient - the DC term - so taking that alone yields one pixel per block, which IS the picture at 1/8
        /// scale. The inverse DCT is skipped outright, and everything after it works on a 64th of the pixels: a 4K
        /// photograph comes out 480x270.</para>
        ///
        /// <para>What it still has to do is read the whole entropy-coded stream, because the coefficients that are
        /// being ignored are what tells the decoder where each block ends. So the saving is the arithmetic, not the
        /// parsing - measured on a 4K wallpaper, roughly a second against three.</para>
        ///
        /// <para>Baseline only for now: a progressive scan spreads even the DC term across passes.</para>
        /// </summary>
        public JpegImage DecodePreview()
        {
            _scaleDivisor = 8;
            try
            {
                return Decode();
            }
            finally
            {
                _scaleDivisor = 1;
            }
        }

        public JpegImage Decode()
        {
            // The frames in this jpeg are loaded into a list. There is
            // usually just one frame except in heirarchial progression where
            // there are multiple frames.
            JpegFrame frame = null;
            JpegImage jpegImage = new JpegImage();

            // The restart interval defines how many MCU's we should have
            // between the 8-modulo restart marker. The restart markers allow
            // us to tell whether or not our decoding process is working
            // correctly, also if there is corruption in the image we can
            // recover with these restart intervals. (See RSTm DRI).
            int resetInterval = 0;

            bool haveMarker = false;
            bool foundJFIF = false;

            List<JpegHeader> headers = new List<JpegHeader>();

            // Loop through until there are no more markers to read in, at
            // that point everything is loaded into the jpegFrames array and
            // can be processed.
            while (true)
            {
                #region Switch over marker types
                switch (marker)
                {
                    case JPEGMarker.APP0:
                    // APP1 is used for EXIF data
                    case JPEGMarker.APP1:
                    // Seldomly, APP2 gets used for extended EXIF, too
                    case JPEGMarker.APP2:
                    case JPEGMarker.APP3:
                    case JPEGMarker.APP4:
                    case JPEGMarker.APP5:
                    case JPEGMarker.APP6:
                    case JPEGMarker.APP7:
                    case JPEGMarker.APP8:
                    case JPEGMarker.APP9:
                    case JPEGMarker.APP10:
                    case JPEGMarker.APP11:
                    case JPEGMarker.APP12:
                    case JPEGMarker.APP13:
                    case JPEGMarker.APP14:
                    case JPEGMarker.APP15:
                    // COM: Comment
                    case JPEGMarker.COM:

                        // Debug.WriteLine(string.Format("Extracting Header, Type={0:X}", marker));

                        JpegHeader header = ExtractHeader();

                        #region Check explicitly for Exif Data

                        if (header.Marker == JPEGMarker.APP1 && header.Data.Length >= 6)
                        {
                            byte[] d = header.Data;

                            if (d[0] == 'E' &&
                                d[1] == 'x' &&
                                d[2] == 'i' &&
                                d[3] == 'f' &&
                                d[4] == 0 &&
                                d[5] == 0)
                            {
                                // Exif.  Do something?
                            }
                        }

                        #endregion

                        #region Check for Adobe header

                        if (header.Data.Length >= 5 && header.Marker == JPEGMarker.APP14)
                        {
                            string asText = Encoding.UTF8.GetString(header.Data, 0, 5);
                            if (asText == "Adobe")
                            {
                                // ADOBE HEADER.  Do anything?
                            }
                        }

                        #endregion

                        headers.Add(header);

                        if (!foundJFIF && marker == JPEGMarker.APP0)
                        {
                            foundJFIF = TryParseJFIF(header.Data);

                            if (foundJFIF) // Found JFIF... do JFIF extension follow?
                            {
                                header.IsJFIF = true;
                                marker = jpegReader.GetNextMarker();

                                // Yes, they do.
                                if (marker == JPEGMarker.APP0)
                                {
                                    header = ExtractHeader();
                                    headers.Add(header);
                                }
                                else // No.  Delay processing this one.
                                    haveMarker = true;
                            }
                        }

                        break;

                    case JPEGMarker.SOF0:
                    case JPEGMarker.SOF2:

                        // SOFn Start of Frame Marker, Baseline DCT - This is the start
                        // of the frame header that defines certain variables that will
                        // be carried out through the rest of the encoding. Multiple
                        // frames are used in a hierarchical system, however most JPEG's
                        // only contain a single frame.

                        // Progressive or baseline?
                        progressive = marker == JPEGMarker.SOF2;

                        jpegFrames.Add(new JpegFrame());
                        frame = jpegFrames[jpegFrames.Count - 1];
                        jpegImage.AddFrame(frame);
                        
                        // Skip the frame length.
                        jpegReader.ReadShort();
                        // Bits precision, either 8 or 12.
                        frame.SetPrecision(jpegReader.ReadByte());
                        // Scan lines (height) 
                        frame.ScanLines = jpegReader.ReadShort();
                        // Scan samples per line (width) 
                        frame.SamplesPerLine = jpegReader.ReadShort();
                        // Number of Color Components (channels).
                        frame.ComponentCount = jpegReader.ReadByte();

                        // Add all of the necessary components to the frame.
                        for (int i = 0; i < frame.ComponentCount; i++)
                        {
                            byte compId = jpegReader.ReadByte();
                            byte sampleFactors = jpegReader.ReadByte();
                            byte qTableId = jpegReader.ReadByte();

                            byte sampleHFactor = (byte)(sampleFactors >> 4);
                            byte sampleVFactor = (byte)(sampleFactors & 0x0f);

                            frame.AddComponent(compId, sampleHFactor, sampleVFactor, qTableId);
                        }
                        break;

                    case JPEGMarker.DHT:

                        // DHT non-SOF Marker - Huffman Table is required for decoding
                        // the JPEG stream, when we receive a marker we load in first
                        // the table length (16 bits), the table class (4 bits), table
                        // identifier (4 bits), then we load in 16 bytes and each byte
                        // represents the count of bytes to load in for each of the 16
                        // bytes. We load this into an array to use later and move on 4
                        // huffman tables can only be used in an image.
                        int huffmanLength = jpegReader.ReadShort() - 2;

                        // Keep looping until we are out of length.
                        int index = huffmanLength;

                        // Multiple tables may be defined within a DHT marker. This
                        // will keep reading until there are no tables left, most
                        // of the time there are just one tables.
                        while (index > 0)
                        {
                            // Read the identifier information and class
                            // information about the Huffman table, then read the
                            // 16 byte codelength in and read in the Huffman values
                            // and put it into table info.
                            byte huffmanInfo = jpegReader.ReadByte();
                            byte tableClass = (byte)(huffmanInfo >> 4);
                            byte huffmanIndex = (byte)(huffmanInfo & 0x0f);
                            short[] codeLength = new short[16];

                            for (int i = 0; i < codeLength.Length; i++)
                                codeLength[i] = jpegReader.ReadByte();

                            int huffmanValueLen = 0;
                            for (int i = 0; i < 16; i++)
                                huffmanValueLen += codeLength[i];
                            index -= huffmanValueLen + 17;

                            short[] huffmanVal = new short[huffmanValueLen];
                            for (int i = 0; i < huffmanVal.Length; i++)
                            {
                                huffmanVal[i] = jpegReader.ReadByte();
                            }
                            // Assign DC Huffman Table.
                            if (tableClass == HuffmanTable.JPEG_DC_TABLE)
                                dcTables[huffmanIndex] = new JpegHuffmanTable(codeLength, huffmanVal);

                            // Assign AC Huffman Table.
                            else if (tableClass == HuffmanTable.JPEG_AC_TABLE)
                                acTables[huffmanIndex] = new JpegHuffmanTable(codeLength, huffmanVal);
                        }
                        break;

                    case JPEGMarker.DQT:

                        // DQT non-SOF Marker - This defines the quantization
                        // coeffecients, this allows us to figure out the quality of
                        // compression and unencode the data. The data is loaded and
                        // then stored in to an array.
                        short quantizationLength = (short)(jpegReader.ReadShort() - 2);
                        for (int j = 0; j < quantizationLength / 65; j++)
                        {
                            byte quantSpecs = jpegReader.ReadByte();
                            int[] quantData = new int[64];
                            if ((byte)(quantSpecs >> 4) == 0)
                            // Precision 8 bit.
                            {
                                for (int i = 0; i < 64; i++)
                                    quantData[i] = jpegReader.ReadByte();

                            }
                            else if ((byte)(quantSpecs >> 4) == 1)
                            // Precision 16 bit.
                            {
                                for (int i = 0; i < 64; i++)
                                    quantData[i] = jpegReader.ReadShort();
                            }
                            qTables[quantSpecs & 0x0f] = new JpegQuantizationTable(quantData);
                        }
                        break;

                    case JPEGMarker.SOS:

                        Debug.WriteLine("Start of Scan (SOS)");

                        // SOS non-SOF Marker - Start Of Scan Marker, this is where the
                        // actual data is stored in a interlaced or non-interlaced with
                        // from 1-4 components of color data, if three components most
                        // likely a YCrCb model, this is a fairly complex process.

                        // Read in the scan length.
                        ushort scanLen = jpegReader.ReadShort();
                        // Number of components in the scan.
                        byte numberOfComponents = jpegReader.ReadByte();
                        byte[] componentSelector = new byte[numberOfComponents];

                        for (int i = 0; i < numberOfComponents; i++)
                        {
                            // Component ID, packed byte containing the Id for the
                            // AC table and DC table.
                            byte componentID = jpegReader.ReadByte();
                            byte tableInfo = jpegReader.ReadByte();

                            int DC = tableInfo >> 4 & 0x0f;
                            int AC = tableInfo & 0x0f;

                            frame.SetHuffmanTables(componentID,
                                                   acTables[(byte)AC],
                                                   dcTables[(byte)DC]);


                            componentSelector[i] = componentID;
                        }

                        byte startSpectralSelection = jpegReader.ReadByte();
                        byte endSpectralSelection = jpegReader.ReadByte();
                        byte successiveApproximation = jpegReader.ReadByte();

                        #region Baseline JPEG Scan Decoding

                        if (!progressive)
                        {
                            frame.DecodeScanBaseline(numberOfComponents, componentSelector, resetInterval, jpegReader, ref marker);
                            haveMarker = true; // use resultant marker for the next switch(..)
                        }

                        #endregion

                        #region Progressive JPEG Scan Decoding

                        if (progressive)
                        {
                            frame.DecodeScanProgressive(
                                successiveApproximation, 
                                startSpectralSelection, 
                                endSpectralSelection,
                                numberOfComponents, 
                                componentSelector, 
                                resetInterval, jpegReader, ref marker);

                            haveMarker = true; // use resultant marker for the next switch(..)
                        }

                        #endregion

                        break;

                    case JPEGMarker.DRI:
                        jpegReader.BaseStream.Seek(2, SeekOrigin.Current);
                        resetInterval = jpegReader.ReadShort();
                        break;

                    /// Defines the number of lines.  (Not usually present)
                    case JPEGMarker.DNL:
                        frame.ScanLines = jpegReader.ReadShort();
                        break;

                    /// End of Image.  Finish the decode.
                    case JPEGMarker.EOI:

                        if (jpegFrames.Count == 0)
                        {
                            throw new NotSupportedException("No JPEG frames could be located.");
                        }
                        else if (jpegFrames.Count > 1) // // Multiple frames, JPEG Hierarchical Frame.
                        {
                            var orderedFrames = jpegFrames.OrderByDescending(f => f.Width * f.Height);
                            frame = orderedFrames.FirstOrDefault(); // Take the biggest frame and continue rasterization
                        }

                        // At preview scale one pixel comes out per 8x8 block, so the raster - and the picture - are an
                        // eighth of the size. Rounded UP: a picture whose edge is not a multiple of 8 still has a
                        // partial block there, and it is a pixel of the answer.
                        var outWidth = (frame.Width + _scaleDivisor - 1) / _scaleDivisor;
                        var outHeight = (frame.Height + _scaleDivisor - 1) / _scaleDivisor;

                        jpegImage.Width = (uint)outWidth;
                        jpegImage.Height = (uint)outHeight;
                        jpegImage.PixelFormat = SurfaceFormat.R8G8B8A8.UNorm;

                        // PREVIEW ONLY WHERE THE BLOCKS FIT WHOLE. A picture whose edge falls inside a block still
                        // decodes correctly at full scale - the surplus is clipped per block - but at preview scale a
                        // block IS a pixel, and the edge column came out carrying a chroma plane from elsewhere. Rather
                        // than ship a preview that is subtly wrong at the edge, this refuses and the caller decodes
                        // normally: slower, and right. Photographs are overwhelmingly multiples of 16, so the refusal
                        // is rare in practice - the wallpaper this was built for is 3840x2160.
                        var mcuW = frame.Scan.MaxH * 8;
                        var mcuH = frame.Scan.MaxV * 8;
                        if (_scaleDivisor != 1 && (frame.Width % mcuW != 0 || frame.Height % mcuH != 0))
                        {
                            throw new PreviewNotAvailableException(
                                $"A {frame.Width}x{frame.Height} picture does not divide into {mcuW}x{mcuH} blocks; " +
                                "decode it at full scale instead.");
                        }

                        var rasterWidth = outWidth;
                        var rasterHeight = outHeight;

                        // Only one frame here
                        byte[][,] raster = ComponentsBuffer.CreateRaster(rasterWidth, rasterHeight, 4);

                        var components = frame.Scan.Components;
                        int totalSteps = components.Count * 3; // Three steps per loop

                        // ONE COMPONENT PER THREAD. Luma and the two chroma planes share nothing at this point: each
                        // dequantizes, transforms and writes into its OWN plane of the raster, so the three passes that
                        // dominate the decode run at once. The tables were assigned before the loop because reading
                        // qTables from several threads is the one thing here that is not obviously safe.
                        for (int i = 0; i < components.Count; i++)
                        {
                            components[i].QuantizationTable = qTables[components[i].quant_id].Table;
                        }

                        var preview = _scaleDivisor == 8;
                        System.Threading.Tasks.Parallel.For(0, components.Count, i =>
                        {
                            var comp = components[i];
                            if (preview)
                            {
                                // One pixel per block, straight from the DC coefficient - no dequantizing of the other
                                // 63, no transform, no upsampling of an 8x8 tile.
                                comp.writeDcScaled(raster, i);
                                return;
                            }

                            comp.quantizeData();
                            comp.idctData();
                            comp.writeDataScaled(raster, i, BlockUpsamplingMode);
                        });


                        // No GC.Collect here. It used to run at exactly the worst moment - right after the decode has
                        // allocated its peak - and a forced full collection stops every thread in the process, not just
                        // this one. Measured at ~100 ms on a 4K photograph, paid by whoever happened to be drawing.
                        // The garbage is collected on its own; asking cannot make it cheaper, only better timed, and
                        // this timing is the worst available.
                        ComponentsBuffer componentsBuffer = null;
                        // Grayscale Color Image (1 Component).
                        if (frame.ComponentCount == 1)
                        {
                            ColorModel cm = new ColorModel() { Colorspace = ColorSpace.Gray, Opaque = true };
                            componentsBuffer = new ComponentsBuffer(cm, raster, outWidth, outHeight);
                        }
                        // YCbCr Color Image (3 Components).
                        else if (frame.ComponentCount == 3)
                        {
                            ColorModel cm = new ColorModel()
                            {
                                Colorspace = ComponentsAreRgb ? ColorSpace.RGB : ColorSpace.YCbCr,
                                Opaque = true
                            };
                            componentsBuffer = new ComponentsBuffer(cm, raster, outWidth, outHeight);
                        }
                        // Possibly CMYK or RGBA ?
                        else
                        {
                            throw new NotSupportedException("Unsupported Color Mode: 4 Component Color Mode found.");
                        }

                        // If needed, convert centimeters to inches.
                        Func<double, double> conv = x =>
                            Units == UnitType.Inches ? x : x / 2.54;

                        componentsBuffer.DensityX = conv(XDensity);
                        componentsBuffer.DensityY = conv(YDensity);

                        componentsBuffer.ChangeColorSpace(ColorSpace.RGB);
                        frame.PixelData = componentsBuffer.GetPixelBuffer();

                        break;

                    // Only SOF0 (baseline) and SOF2 (progressive) are supported by FJCore
                    case JPEGMarker.SOF1:
                    case JPEGMarker.SOF3:
                    case JPEGMarker.SOF5:
                    case JPEGMarker.SOF6:
                    case JPEGMarker.SOF7:
                    case JPEGMarker.SOF9:
                    case JPEGMarker.SOF10:
                    case JPEGMarker.SOF11:
                    case JPEGMarker.SOF13:
                    case JPEGMarker.SOF14:
                    case JPEGMarker.SOF15:
                        throw new NotSupportedException("Unsupported codec type.");

                    default: break;  // ignore

                }

                #endregion switch over markers

                if (haveMarker) haveMarker = false;
                else
                {
                    try
                    {
                        marker = jpegReader.GetNextMarker();
                    }
                    catch (EndOfStreamException)
                    {
                        break; /* done reading the file */
                    }
                }
            }

            return jpegImage;
        }

        public IList<JpegHeader> ExtractHeaders()
        {
            // Extracts only the header (EXIF/XMP) data from the JPEG. This 
            // method is useful if the JPEG is to be manipulated in any 
            // way other than resizing (such as cropping), so that the metadata
            // can be easily re-added to the outputted JPEG. The separate call
            // is useful as loading the entire file just to pull the headers
            // is a very time consuming process

            bool haveMarker = false;
            bool foundJFIF = false;

            var headers = new List<JpegHeader>();

            // Loop through until there are no more markers to read in, at
            // that point the headers have been fully extracted
            while (true)
            {
                #region Switch over marker types
                switch (marker)
                {
                    case JPEGMarker.APP0:
                    // APP1 is used for EXIF data
                    case JPEGMarker.APP1:
                    // Seldomly, APP2 gets used for extended EXIF, too
                    case JPEGMarker.APP2:
                    case JPEGMarker.APP3:
                    case JPEGMarker.APP4:
                    case JPEGMarker.APP5:
                    case JPEGMarker.APP6:
                    case JPEGMarker.APP7:
                    case JPEGMarker.APP8:
                    case JPEGMarker.APP9:
                    case JPEGMarker.APP10:
                    case JPEGMarker.APP11:
                    case JPEGMarker.APP12:
                    case JPEGMarker.APP13:
                    case JPEGMarker.APP14:
                    case JPEGMarker.APP15:
                    // COM: Comment
                    case JPEGMarker.COM:

                        // We only want to extract the headers so they can be round tripped
                        // easily with the JPEG needs to be resized

                        JpegHeader header = ExtractHeader();

                        #region Check explicitly for Exif Data

                        if (header.Marker == JPEGMarker.APP1 && header.Data.Length >= 6)
                        {
                            byte[] d = header.Data;

                            if (d[0] == 'E' &&
                                d[1] == 'x' &&
                                d[2] == 'i' &&
                                d[3] == 'f' &&
                                d[4] == 0 &&
                                d[5] == 0)
                            {
                                // Exif.  Do something?
                            }
                        }

                        #endregion

                        #region Check for Adobe header

                        if (header.Data.Length >= 5 && header.Marker == JPEGMarker.APP14)
                        {
                            string asText = Encoding.UTF8.GetString(header.Data, 0, 5);
                            if (asText == "Adobe")
                            {
                                // ADOBE HEADER.  Do anything?
                            }
                        }

                        #endregion

                        headers.Add(header);

                        if (!foundJFIF && marker == JPEGMarker.APP0)
                        {
                            foundJFIF = TryParseJFIF(header.Data);

                            if (foundJFIF) // Found JFIF... do JFIF extension follow?
                            {
                                header.IsJFIF = true;
                                marker = jpegReader.GetNextMarker();

                                // Yes, they do.
                                if (marker == JPEGMarker.APP0)
                                {
                                    header = ExtractHeader();
                                    headers.Add(header);
                                }
                                else // No.  Delay processing this one.
                                    haveMarker = true;
                            }
                        }

                        break;

                    case JPEGMarker.SOF0:
                    case JPEGMarker.SOF2:
                        break;

                    case JPEGMarker.DHT:
                        break;

                    case JPEGMarker.DQT:
                        break;

                    case JPEGMarker.SOS:
                        break;

                    case JPEGMarker.DRI:
                        break;

                    case JPEGMarker.DNL:
                        break;

                    case JPEGMarker.EOI:
                        break;

                    case JPEGMarker.SOF1:
                    case JPEGMarker.SOF3:
                    case JPEGMarker.SOF5:
                    case JPEGMarker.SOF6:
                    case JPEGMarker.SOF7:
                    case JPEGMarker.SOF9:
                    case JPEGMarker.SOF10:
                    case JPEGMarker.SOF11:
                    case JPEGMarker.SOF13:
                    case JPEGMarker.SOF14:
                    case JPEGMarker.SOF15:
                        throw new NotSupportedException("Unsupported codec type.");

                    default: break;  // ignore

                }

                #endregion switch over markers

                if (haveMarker) haveMarker = false;
                else
                {
                    try
                    {
                        marker = jpegReader.GetNextMarker();
                    }
                    catch (EndOfStreamException)
                    {
                        break; /* done reading the file */
                    }
                }
            }

            return headers;
        }

        private JpegHeader ExtractHeader()
        {
            #region Extract the header

            int length = jpegReader.ReadShort() - 2;
            byte[] data = new byte[length];
            jpegReader.Read(data, 0, length);

            #endregion

            JpegHeader header = new JpegHeader()
            {
                Marker = marker,
                Data = data
            };
            return header;
        }
    }
}
