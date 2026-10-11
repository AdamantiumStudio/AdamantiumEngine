using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Adamantium.Core;
using Adamantium.Mathematics;

namespace Adamantium.Imaging.Gif
{
    public class GifDecoder
    {
        private const string GIFHeader = "GIF89a";

        private int _frameIndex = 0;

        public GifDecoder()
        {
        }

        public GifImage Decode(UnmanagedMemoryStream stream)
        {
            if (!ReadGIFHeader(stream))
            {
                throw new ArgumentException("Given file is not a GIF file");
            }

            var gifImage = new GifImage();
            ScreenDescriptor screenDescriptor = new ScreenDescriptor();
            screenDescriptor.Width = stream.ReadUInt16();
            screenDescriptor.Height = stream.ReadUInt16();
            screenDescriptor.Fields = (byte)stream.ReadByte();
            screenDescriptor.BackgroundColorIndex = (byte)stream.ReadByte();
            screenDescriptor.PixelAspectRatio = (byte)stream.ReadByte();

            /* Color Space's Depth */
            gifImage.ColorDepth = (byte)(((screenDescriptor.Fields >> 4) & 7) + 1);
            /* Ignore Sort Flag. */
            /* GCT Size */
            var gctSize = 1 << ((screenDescriptor.Fields & 0x07) + 1);

            /* Presence of GCT (global color table) */
            if ((screenDescriptor.Fields & 0x80) != 0)
            {
                var colorTable = new byte[3 * gctSize];
                stream.ReadExactly(colorTable, 0, colorTable.Length);
                int offset = 0;
                for (int i = 0; i < gctSize; i++)
                {
                    gifImage.GlobalColorTable.Add(new ColorRGB(colorTable[offset], colorTable[offset + 1], colorTable[offset + 2]));
                    offset += 3;
                }
            }

            gifImage.Descriptor = screenDescriptor;

            GifChunkCodes blockType = GifChunkCodes.None;
            while (blockType != GifChunkCodes.Trailer)
            {
                blockType = (GifChunkCodes)stream.ReadByte();

                switch (blockType)
                {
                    case GifChunkCodes.ImageDescriptor:
                        ProcessImageDescriptor(stream, gifImage, _frameIndex);
                        _frameIndex++;
                        break;
                    case GifChunkCodes.ExtensionIntroducer:
                        ProcessExtension(stream, gifImage);
                        break;
                    case GifChunkCodes.Trailer:
                        break;
                }
            }

            return gifImage;
        }

        // private Image DecodeAllFrames(GifImage gif)
        // {
        //     for (int i = 0; i < gif.Frames.Count; i++)
        //     {
        //         var frame = gif.Frames[i];
        //         
        //         GetImageFromIndexStream(frame, i, gif);
        //     }
        //
        //     for (int i = 0; i < gif.Frames.Count; i++)
        //     {
        //         GifFrame frame = gif.Frames[i];
        //         var handle = GCHandle.Alloc(frame.RawPixels, GCHandleType.Pinned);
        //         Utilities.CopyMemory(img.pixelBuffers[i].DataPointer, handle.AddrOfPinnedObject(), frame.RawPixels.Length);
        //         handle.Free();
        //     }
        //     return img;
        // }

        /*
            interlaced GIF images are stored as 4 separated images
            1. containing every 8th line of image (1/8 of size)
            2. containing every missing 4th line of image (1/8 of size)
            3. containing every missing even line of image (1/4 of size)
            4. containing every odd line of image (1/2 of size)
            http://www.fileformat.info/format/gif/egff.htm
         */
        internal static int[] Deinterlace(GifFrame frame, int[] indexData)
        {
            var width = frame.Descriptor.Width;
            var height = frame.Descriptor.Height;
            int i, j;
            int[] interlacedRowTable = new int[height];
            int[] deinterlacedTable = new int[height];
            for (i = 0; i < height; ++i)
            {
                interlacedRowTable[i] = i;
            }

            j = 0;
            for (i = 0; i < height; i += 8, ++j) /* Interlace Pass 1 */
            {
                deinterlacedTable[i] = interlacedRowTable[j];
            }
            for (i = 4; i < height; i += 8, ++j) /* Interlace Pass 2 */
            {
                deinterlacedTable[i] = interlacedRowTable[j];
            }
            for (i = 2; i < height; i += 4, ++j) /* Interlace Pass 3 */
            {
                deinterlacedTable[i] = interlacedRowTable[j];
            }
            for (i = 1; i < height; i += 2, ++j) /* Interlace Pass 4 */
            {
                deinterlacedTable[i] = interlacedRowTable[j];
            }

            int[] deinterlacedIndexData = new int[width * height];
            for (i = 0; i < height; i++)
            {
                var srcIndex = deinterlacedTable[i] * width;
                var dstIndex = i * width;
                Array.Copy(indexData, srcIndex, deinterlacedIndexData, dstIndex, width);
            }

            return deinterlacedIndexData;
        }

        /// <summary>
        /// Read GIF frame
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="gifImage"></param>
        private static void ProcessImageDescriptor(Stream stream, GifImage gifImage, int frameIndex)
        {
            var descriptor = new GifImageDescriptor();
            descriptor.OffsetLeft = stream.ReadUInt16();
            descriptor.OffsetTop = stream.ReadUInt16();
            descriptor.Width = stream.ReadUInt16();
            descriptor.Height = stream.ReadUInt16();
            descriptor.Fields = (byte)stream.ReadByte();
            int interlace = descriptor.Fields & 0x40;
            GifFrame frame = new GifFrame(frameIndex);
            frame.Interlaced = Convert.ToBoolean(interlace);

            if ((descriptor.Fields & 0x80) != 0)
            {
                var size = 1 << ((descriptor.Fields & 0x07) + 1);
                //Read local color table
                var colorTable = new byte[3 * size];
                stream.ReadExactly(colorTable, 0, colorTable.Length);
                int offset = 0;
                frame.ColorTable = new ColorRGB[size];
                for (int i = 0; i < size; i++)
                {
                    frame.ColorTable[i] = new ColorRGB(colorTable[offset], colorTable[offset + 1], colorTable[offset + 2]);
                    offset += 3;
                }
            }
            else
            {
                frame.ColorTable = gifImage.GlobalColorTable.ToArray();
            }

            frame.Descriptor = descriptor;
            ReadImageData(stream, frame);
            gifImage.CurrentFrame = frame;
            gifImage.AddFrame(frame);
        }

        /// Decompress image pixels.
        private static void ReadImageData(Stream stream, GifFrame frame)
        {
            frame.LzwMinimumCodeSize = (byte)stream.ReadByte();
            if (frame.LzwMinimumCodeSize > 8)
            {
                throw new Exception($"LZW minimum code could not be more than 8. Current value is {frame.LzwMinimumCodeSize}");
            }
            byte blockSize;

            // Everything following are data sub-blocks, until a 0-sized block is
            // encountered.

            while (true)
            {
                var result = stream.ReadByte();
                if (result < 0)
                {
                    break;
                }

                blockSize = (byte)result;

                if (blockSize == 0)  // end of sub-blocks
                {
                    break;
                }

                var bytes = stream.ReadBytes(blockSize);
                frame.CompressedData.AddRange(bytes);
            }
        }

        private static void ProcessExtension(Stream stream, GifImage gifImage)
        {
            var extensionCode = (GifChunkCodes)stream.ReadByte();
            int blockSize = stream.ReadByte();
            var position = stream.Position;
            switch (extensionCode)
            {
                case GifChunkCodes.GraphicControl:
                    var graphicControlExtension = new GraphicControlExtension();
                    graphicControlExtension.Fields = (byte)stream.ReadByte();
                    graphicControlExtension.DelayTime = stream.ReadUInt16();
                    graphicControlExtension.TransparentColorIndex = (byte)stream.ReadByte();
                    graphicControlExtension.TransparencyAvailable = Convert.ToBoolean(graphicControlExtension.Fields & 0x07);
                    graphicControlExtension.DisposalMethod = (DisposalMethod)(graphicControlExtension.Fields & 0x03);
                    if (gifImage.CurrentFrame != null)
                    {
                        gifImage.CurrentFrame.GraphicControlExtension = graphicControlExtension;
                    }
                    break;
                case GifChunkCodes.ApplicationExtension:
                    var applicationExtension = new ApplicationExtension();
                    applicationExtension.ApplicationId = Encoding.ASCII.GetString(stream.ReadBytes(8));
                    applicationExtension.Version = Encoding.ASCII.GetString(stream.ReadBytes(3));
                    gifImage.ApplicationExtension = applicationExtension;
                    if (applicationExtension.ApplicationId == "NETSCAPE")
                    {
                        var extBlockSize = stream.ReadByte();
                        stream.ReadBytes(extBlockSize);
                    }
                    else if (applicationExtension.Version == "XMP")
                    {
                        var @byte = (byte)stream.ReadByte();
                        var bytes = new List<byte>() { @byte};
                        while(@byte != 0)
                        {
                            @byte = (byte)stream.ReadByte();
                            bytes.Add(@byte);
                        }
                        //var resultArray = bytes.ToArray()[..^257];
                        blockSize += bytes.Count;
                    }
                    break;
                case GifChunkCodes.CommentExtension:
                    // comment extension; do nothing - all the data is in the
                    // sub-blocks that follow.
                    break;
                case GifChunkCodes.PlainTextExtension:
                    var plainTextExtension = new PlainTextExtension();
                    plainTextExtension.Left = stream.ReadUInt16();
                    plainTextExtension.Top = stream.ReadUInt16();
                    plainTextExtension.Width = stream.ReadUInt16();
                    plainTextExtension.Height = stream.ReadUInt16();
                    plainTextExtension.CellWidth = (byte)stream.ReadByte();
                    plainTextExtension.CellHeight = (byte)stream.ReadByte();
                    plainTextExtension.ForegroundColor = (byte)stream.ReadByte();
                    plainTextExtension.BckgroundColor = (byte)stream.ReadByte();
                    break;
                default:
                    break;
            }
            stream.Position = position;
            stream.Position += blockSize;
        }

        private static bool ReadGIFHeader(Stream stream)
        {
            var bytes = stream.ReadBytes(6);
            var headerString = Encoding.ASCII.GetString(bytes);
            if (headerString != GIFHeader)
            {
                return false;
            }

            return true;
        }
    }
}
