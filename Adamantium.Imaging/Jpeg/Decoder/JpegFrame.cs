/// Copyright (c) 2008 Jeffrey Powers for Fluxcapacity Open Source.
/// Under the MIT License, details: License.txt.

using System;
using Adamantium.Imaging.Jpeg.IO;

namespace Adamantium.Imaging.Jpeg.Decoder
{
    internal class JpegFrame
    {
        public static byte JPEG_COLOR_GRAY = 1;
        public static byte JPEG_COLOR_RGB = 2;
        public static byte JPEG_COLOR_YCbCr = 3;
        public static byte JPEG_COLOR_CMYK = 4;

        public byte precision = 8;
        public byte colorMode = JPEG_COLOR_YCbCr;

        public ushort Width { get; internal set; }
        public ushort Height { get; internal set; }

        public JpegScan Scan = new JpegScan();

        public int SizeInBytes => Width * Height * ComponentCount;
        
        public byte[] PixelData { get; set; }


        public void AddComponent(byte componentID, 
            byte sampleHFactor, 
            byte sampleVFactor, 
            byte quantizationTableID)
        {
            Scan.AddComponent(componentID, sampleHFactor, sampleVFactor, quantizationTableID, colorMode);
        }

        public void SetPrecision(byte data) { precision = data; }

        public ushort ScanLines { set => Height = value; }
        public ushort SamplesPerLine { set => Width = value; }

        public byte ColorMode =>
            ComponentCount == 1 ?
                JPEG_COLOR_GRAY :
                JPEG_COLOR_YCbCr;

        public byte ComponentCount { get; set; }

        public void SetHuffmanTables(byte componentID, JpegHuffmanTable ACTable, JpegHuffmanTable DCTable)
        {
            JpegComponent comp = Scan.GetComponentById(componentID);

            if (DCTable != null) comp.setDCTable(DCTable);
            if (ACTable != null) comp.setACTable(ACTable);
        }

        public void DecodeScanBaseline(byte numberOfComponents, byte[] componentSelector, int resetInterval, JPEGBinaryReader jpegReader, ref byte marker)
        {
            // Set the decode function for all the components
            for (int compIndex = 0; compIndex < numberOfComponents; compIndex++)
            {
                JpegComponent comp = Scan.GetComponentById(componentSelector[compIndex]);
                comp.Decode = comp.DecodeBaseline;
            }

            DecodeScan(numberOfComponents, componentSelector, resetInterval, jpegReader, ref marker);
        }

        private long ScanUnits(bool interleaved, byte[] componentSelector)
        {
            if (interleaved)
            {
                return (Width + 8L * Scan.MaxH - 1) / (8L * Scan.MaxH) * ((Height + 8L * Scan.MaxV - 1) / (8L * Scan.MaxV));
            }

            var component = Scan.GetComponentById(componentSelector[0]);
            var width = ((long)Width * component.factorH + Scan.MaxH - 1) / Scan.MaxH;
            var height = ((long)Height * component.factorV + Scan.MaxV - 1) / Scan.MaxV;
            return (width + 7) / 8 * ((height + 7) / 8);
        }

        private static bool IsRestart(byte marker) => marker is >= JPEGMarker.RST0 and <= JPEGMarker.RST7;

        private void Restart(byte numberOfComponents, byte[] componentSelector, JPEGBinaryReader jpegReader)
        {
            jpegReader.DiscardBits();
            jpegReader.eob_run = 0;
            for (int compIndex = 0; compIndex < numberOfComponents; compIndex++)
            {
                Scan.GetComponentById(componentSelector[compIndex]).resetInterval();
            }
        }

        private int McusPerRow(JpegComponent c)
        {
            return ((Width * c.factorH + (Scan.MaxH - 1)) / Scan.MaxH + 7) / 8;
        }

        private void DecodeScan(byte numberOfComponents, byte[] componentSelector, int resetInterval, JPEGBinaryReader jpegReader, ref byte marker)
        {
            //TODO: not necessary
            jpegReader.eob_run = 0;

            int mcuIndex = 0;
            int mcuTotalIndex = 0;

            // This loops through until a MarkerTagFound exception is
            // found, if the marker tag is a RST (Restart Marker) it
            // simply skips it and moves on this system does not handle
            // corrupt data streams very well, it could be improved by
            // handling misplaced restart markers.

            int h = 0, v = 0;
            int x = 0;

            var interleaved = numberOfComponents != 1;
            var total = ScanUnits(interleaved, componentSelector);
            long decoded = 0;
            var inInterval = 0;
            while (decoded < total)
            {
               if (resetInterval > 0 && inInterval == resetInterval)
               {
                   jpegReader.DiscardBits();
                   var next = jpegReader.GetNextMarker();
                   if (!IsRestart(next))
                   {
                       marker = next;
                       return;
                   }

                   Restart(numberOfComponents, componentSelector, jpegReader);
                   inInterval = 0;
                   if (interleaved)
                   {
                       mcuIndex = 0;
                   }
               }

               try
               {
                   // Loop though capturing MCU, instruct each
                   // component to read in its necessary count, for
                   // scaling factors the components automatically
                   // read in how much they need

                   // Sec A.2.2 from CCITT Rec. T.81 (1992 E)
                   if (!interleaved)
                   {
                       JpegComponent comp = Scan.GetComponentById(componentSelector[0]);

                       comp.SetBlock(mcuIndex);

                       comp.DecodeMCU(jpegReader, h, v);

                       int mcus_per_line = McusPerRow(comp);
                       int blocks_per_line = (int)Math.Ceiling((double)Width / (8 * comp.factorH));


                       // TODO: Explain the non-interleaved scan ------

                       h++; x++;

                       if (h == comp.factorH)
                       {
                           h = 0; mcuIndex++;
                       }

                       if (x % mcus_per_line == 0)
                       {
                           x = 0;
                           v++;

                           if (v == comp.factorV)
                           {
                               if (h != 0) { mcuIndex++; h = 0; }
                               v = 0;
                           }
                           else
                           {
                               mcuIndex -= blocks_per_line;

                               // we were mid-block
                               if (h != 0) { mcuIndex++; h = 0; }
                           }
                       }

                       // -----------------------------------------------

                   }
                   else // Components are interleaved
                   {
                       for (int compIndex = 0; compIndex < numberOfComponents; compIndex++)
                       {
                           JpegComponent comp = Scan.GetComponentById(componentSelector[compIndex]);
                           comp.SetBlock(mcuTotalIndex);

                           for (int j = 0; j < comp.factorV; j++)
                           for (int i = 0; i < comp.factorH; i++)
                           {
                               comp.DecodeMCU(jpegReader, i, j);
                           }
                       }

                       mcuIndex++;
                       mcuTotalIndex++;
                   }

                   decoded++;
                   inInterval++;
               }
               catch (JpegMarkerFoundException ex)
               {
                   marker = ex.Marker;
                   if (!IsRestart(marker))
                   {
                       return;
                   }

                   if (interleaved && resetInterval > 0)
                   {
                       for (int compIndex = 0; compIndex < numberOfComponents; compIndex++)
                       {
                           JpegComponent comp = Scan.GetComponentById(componentSelector[compIndex]);
                           if (compIndex > 1)
                               comp.padMCU(mcuTotalIndex, resetInterval - mcuIndex);
                       }

                       mcuTotalIndex += resetInterval - mcuIndex;
                       decoded += resetInterval - mcuIndex;
                       mcuIndex = 0;
                   }

                   Restart(numberOfComponents, componentSelector, jpegReader);
                   inInterval = 0;
               }
            }

            jpegReader.DiscardBits();
            marker = jpegReader.GetNextMarker();
        }

        public void DecodeScanProgressive(byte successiveApproximation, 
            byte startSpectralSelection, 
            byte endSpectralSelection,
            byte numberOfComponents, 
            byte[] componentSelector, 
            int resetInterval, 
            JPEGBinaryReader jpegReader, 
            ref byte marker)
        {
            byte successiveHigh = (byte)(successiveApproximation >> 4);
            byte successiveLow = (byte)(successiveApproximation & 0x0f);

            if (startSpectralSelection > endSpectralSelection || endSpectralSelection > 63)
                throw new Exception("Bad spectral selection.");

            bool dcOnly = startSpectralSelection == 0;
            bool refinementScan = successiveHigh != 0;

            if (dcOnly) // DC scan
            {
                if (endSpectralSelection != 0)
                    throw new Exception("Bad spectral selection for DC only scan.");
            }
            else // AC scan
            {
                if (numberOfComponents > 1)
                    throw new Exception("Too many components for AC scan!");
            }

            // Set the decode function for all the components
            // TODO: set this for the scan and let the component figure it out
            for (int compIndex = 0; compIndex < numberOfComponents; compIndex++)
            {
                JpegComponent comp = Scan.GetComponentById(componentSelector[compIndex]);

                comp.successiveLow = successiveLow;

                if (dcOnly)
                {
                    if (refinementScan) // DC refine
                        comp.Decode = comp.DecodeDCRefine;
                    else  //               DC first
                        comp.Decode = comp.DecodeDCFirst;
                }
                else
                {
                    comp.spectralStart = startSpectralSelection;
                    comp.spectralEnd = endSpectralSelection;

                    if (refinementScan) // AC refine
                        comp.Decode = comp.DecodeACRefine;
                    else  //               AC first
                        comp.Decode = comp.DecodeACFirst;
                }
            }

            DecodeScan(numberOfComponents, componentSelector, resetInterval, jpegReader, ref marker);

        }
    }

}
