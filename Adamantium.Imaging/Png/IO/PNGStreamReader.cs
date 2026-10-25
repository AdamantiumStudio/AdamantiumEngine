using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Imaging.Png.Chunks;

namespace Adamantium.Imaging.Png.IO
{
    public unsafe class PNGStreamReader : UnmanagedMemoryStream
    {
        internal static byte[] PngHeader = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public PNGStreamReader(IntPtr pSource, ulong size) : base((byte*)pSource, (long)size)
        {
        }

        public byte[] ReadBytes(int count)
        {
            var buffer = new byte[count];

            if (Read(buffer, 0, count) != count)
                throw new Exception("End reached.");

            return buffer;
        }

        public uint ErrorCode { get; private set; }

        public ushort ReadUInt16()
        {
            var bytes = ReadBytes(2);
            return BitConverter.ToUInt16(bytes.Reverse().ToArray(), 0);
        }

        public uint ReadUInt32()
        {
            var bytes = ReadBytes(4);
            return BitConverter.ToUInt32(bytes.Reverse().ToArray(), 0);
        }

        public int ReadInt32()
        {
            var bytes = ReadBytes(4);
            return BitConverter.ToInt32(bytes.Reverse().ToArray(), 0);
        }

        public uint ReadChunkSize()
        {
            return ReadUInt32();
        }

        public string ReadChunkType()
        {
            var bytes = ReadBytes(4);
            return Encoding.ASCII.GetString(bytes);
        }

        internal bool ReadPNGSignature()
        {
            var bytes = ReadBytes(8);
            for (int i = 0; i < bytes.Length; ++i)
            {
                if (bytes[i] != PngHeader[i])
                {
                    return false;
                }
            }
            return true;
        }

        internal IHDR ReadIHDR()
        {
            IHDR header = new IHDR();
            header.Width = ReadInt32();
            header.Height = ReadInt32();
            header.BitDepth = (byte)ReadByte();
            header.ColorType = (PngColorType)ReadByte();
            header.CompressionMethod = (byte)ReadByte();
            header.FilterMethod = (byte)ReadByte();
            header.InterlaceMethod = (InterlaceMethod)ReadByte();
            header.CRC = ReadUInt32();
            Position = 12;
            var data = ReadBytes(17);
            header.CheckSum = Crc32.CalculateCheckSum(data);

            return header;
        }

        internal sRGB ReadsRGB()
        {
            var pos = Position - 4;
            sRGB srgb = new sRGB();
            srgb.RenderingIntent = (RenderingIntent)ReadByte();
            srgb.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(5);
            srgb.CheckSum = Crc32.CalculateCheckSum(bytes);
            return srgb;
        }

        internal gAMA ReadgAMA()
        {
            var pos = Position - 4;
            gAMA gama = new gAMA();
            var data = ReadBytes(4);
            gama.Gamma = 16777216u * data[0] + 65536u * data[1] + 256u * data[2] + data[3];
            gama.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(5);
            gama.CheckSum = Crc32.CalculateCheckSum(bytes);
            return gama;
        }

        internal pHYs ReadpHYs()
        {
            var pos = Position - 4;
            pHYs phys = new pHYs();
            phys.PhysX = ReadUInt32();
            phys.PhysX = ReadUInt32();
            phys.Unit = (Unit)ReadByte();
            phys.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(13);
            phys.CheckSum = Crc32.CalculateCheckSum(bytes);
            return phys;
        }

        internal tEXt ReadtEXt(PngState state, uint chunkLength)
        {
            var pos = Position - 4;
            var data = ReadBytes((int)chunkLength);
            var text = new tEXt();

            int length = 0;
            while (length < chunkLength && data[length] != 0) ++length;

            if (length < 1 || length > 79)
            {
                /*keyword too short or long*/
                state.Error = 89;
            }

            text.Key = Encoding.ASCII.GetString(data, 0, length);
            length++;
            text.Text = Encoding.ASCII.GetString(data, length, data.Length - length);
            text.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            text.CheckSum = Crc32.CalculateCheckSum(bytes);

            return text;
        }

        internal iCCP ReadiCCP(PngState state, PngDecoder decoder, uint chunkLength)
        {
            var pos = Position - 4;
            var data = ReadBytes((int)chunkLength);
            var iccp = new iCCP();

            int length = 0;
            while (length < chunkLength && data[length] != 0) ++length;
            if (length + 2 >= chunkLength)
            {
                /*no null termination, corrupt?*/
                state.Error = 75;
            }

            if (length < 1 || length > 79)
            {
                /*keyword too short or long*/
                state.Error = 89;
            }

            iccp.ICCPName = Encoding.ASCII.GetString(data, 0, length);
            length++;
            if (data[length] != 0)
            {
                /*the 0 byte indicating compression must be 0*/
                state.Error = 72;
            }
            length++;

            var slice = data[length..];
            List<byte> decoded = new List<byte>();
            PngCompressor compressor = new PngCompressor();
            state.Error = compressor.Decompress(slice, state.DecoderSettings, decoded);
            if (state.Error == 0)
            {
                iccp.Profile = decoded.ToArray();
            }

            iccp.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            iccp.CheckSum = Crc32.CalculateCheckSum(bytes);

            return iccp;
        }

        internal cHRM ReadcHRM()
        {
            var pos = Position - 4;
            var chrm = new cHRM();
            chrm.WhitePointX = ReadUInt32();
            chrm.WhitePointY = ReadUInt32();
            chrm.RedX = ReadUInt32();
            chrm.RedY = ReadUInt32();
            chrm.GreenX = ReadUInt32();
            chrm.GreenY = ReadUInt32();
            chrm.BlueX = ReadUInt32();
            chrm.BlueY = ReadUInt32();

            chrm.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(36);
            chrm.CheckSum = Crc32.CalculateCheckSum(bytes);
            return chrm;
        }

        internal iTXt ReadiTXt(PngState state, PngDecoder decoder, uint chunkLength)
        {
            if (chunkLength < 5)
            {
                /*iTXt chunk too short*/
                state.Error = 5;
                return null;
            }

            var pos = Position - 4;
            var data = ReadBytes((int)chunkLength);
            var itxt = new iTXt();
            byte compressed;

            int length = 0;
            while (length < chunkLength && data[length] != 0) ++length;
            if (length + 2 >= chunkLength)
            {
                /*no null termination, corrupt?*/
                state.Error = 75;
            }

            if (length < 1 || length > 79)
            {
                /*keyword too short or long*/
                state.Error = 89;
            }

            itxt.Key = Encoding.ASCII.GetString(data, 0, length);
            length++;
            compressed = data[length];
            if (data[length + 1] != 0)
            {
                /*the 0 byte indicating compression must be 0*/
                state.Error = 72;
            }

            /*even though it's not allowed by the standard, no error is thrown if
            there's no null termination char, if the text is empty for the next 3 texts*/
            length += 1;
            var begin = length;
            length = 0;
            for (int i = begin; i < chunkLength && data[i] != 0; ++i) ++length;

            var langTag = data[begin..(begin + length)];
            itxt.LangTag = Encoding.ASCII.GetString(langTag);

            begin += length + 1;
            length = 0;
            for (int i = begin; i < chunkLength && data[i] != 0; ++i) ++length;
            var transKey = data[begin..(begin + length)];
            itxt.TransKey = Encoding.ASCII.GetString(transKey);

            if (length == 0)
            {
                length++;
            }

            begin += length + 1;

            var slice = data[begin..];
            List<byte> decoded = new List<byte>();
            if (compressed == 1)
            {
                PngCompressor compressor = new PngCompressor();
                state.Error = compressor.Decompress(slice, state.DecoderSettings, decoded);
                if (state.Error > 0)
                {
                    return null;
                }
            }
            else
            {
                decoded.AddRange(slice);
            }

            itxt.Text = Encoding.ASCII.GetString(decoded.ToArray());
            itxt.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            itxt.CheckSum = Crc32.CalculateCheckSum(bytes);

            state.InfoPng.InternationalText = itxt;
            return itxt;
        }

        internal zTXt ReadzTXt(PngState state, PngDecoder decoder, uint chunkLength)
        {
            var pos = Position - 4;
            var data = ReadBytes((int)chunkLength);
            var ztxt = new zTXt();

            int length = 0;
            while (length < chunkLength && data[length] != 0) ++length;
            if (length + 2 >= chunkLength)
            {
                /*no null termination, corrupt?*/
                state.Error = 75;
            }

            if (length < 1 || length > 79)
            {
                /*keyword too short or long*/
                state.Error = 89;
            }

            ztxt.Key = Encoding.ASCII.GetString(data, 0, length);
            length++;
            if (data[length] != 0)
            {
                /*the 0 byte indicating compression must be 0*/
                state.Error = 72;
                return null;
            }

            var begin = length + 1;
            if (begin > chunkLength)
            {
                /*no null termination, corrupt?*/
                state.Error = 75;
                return null;
            }

            List<byte> decoded = new List<byte>();
            var slice = data[begin..];
            PngCompressor compressor = new PngCompressor();
            state.Error = compressor.Decompress(slice, state.DecoderSettings, decoded);

            if (state.Error > 0)
            {
                return null;
            }
            ztxt.Text = Encoding.ASCII.GetString(decoded.ToArray());

            ztxt.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            ztxt.CheckSum = Crc32.CalculateCheckSum(bytes);

            return ztxt;
        }

        internal tIME ReadtIME(PngState state)
        {
            var pos = Position - 4;
            tIME time = new tIME();
            time.Year = ReadUInt16();
            time.Month = (byte)ReadByte();
            time.Day = (byte)ReadByte();
            time.Hour = (byte)ReadByte();
            time.Minute = (byte)ReadByte();
            time.Second = (byte)ReadByte();

            time.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(11);
            time.CheckSum = Crc32.CalculateCheckSum(bytes);

            state.InfoPng.Time = time;
            state.InfoPng.IsTimeDefined = true;

            return time;
        }

        internal bKGD ReadbKGD(PngState state, uint chunkLength)
        {
            var pos = Position - 4;
            var info = state.InfoPng;
            var bkgd = new bKGD();
            if (info.ColorMode.ColorType == PngColorType.Palette)
            {
                if (chunkLength != 1)
                {
                    /*error: this chunk must be 1 byte for indexed color image*/
                    state.Error = 43;
                    return null;
                }

                var colorByte = ReadByte();
                if (colorByte >= info.ColorMode.PaletteSize)
                {
                    /*error: invalid palette index, or maybe this chunk appeared before PLTE*/
                    state.Error = 103;
                    return null;
                }

                info.IsBackgroundDefined = true;
                bkgd.BackgroundR = bkgd.BackgroundG = bkgd.BackgroundB = (uint)colorByte;
                info.BackgroundR = info.BackgroundG = info.BackgroundB = (uint)colorByte;
            }
            else if (info.ColorMode.ColorType == PngColorType.Gray || info.ColorMode.ColorType == PngColorType.GrayAlpha)
            {
                /*error: this chunk must be 2 bytes for grayscale image*/
                if (chunkLength != 2)
                {
                    state.Error = 44;
                }

                /*the values are truncated to bitdepth in the PNG file*/
                info.IsBackgroundDefined = true;
                var color = ReadUInt16();
                bkgd.BackgroundR = bkgd.BackgroundG = bkgd.BackgroundB = color;
                info.BackgroundR = info.BackgroundG = info.BackgroundB = color;
            }
            else if (info.ColorMode.ColorType == PngColorType.RGB || info.ColorMode.ColorType == PngColorType.RGBA)
            {
                /*error: this chunk must be 6 bytes for grayscale image*/
                if (chunkLength != 6)
                {
                    state.Error = 45;
                    return null;
                }

                /*the values are truncated to bitdepth in the PNG file*/
                info.IsBackgroundDefined = true;
                var redColor = ReadUInt16();
                var greenColor = ReadUInt16();
                var blueColor = ReadUInt16();
                info.IsBackgroundDefined = true;
                bkgd.BackgroundR = redColor;
                bkgd.BackgroundG = greenColor;
                bkgd.BackgroundB = blueColor;
                info.BackgroundR = bkgd.BackgroundR;
                info.BackgroundG = bkgd.BackgroundG;
                info.BackgroundB = bkgd.BackgroundB;
            }

            bkgd.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            bkgd.CheckSum = Crc32.CalculateCheckSum(bytes);

            return bkgd;
        }

        internal tRNS ReadtRNS(PngState state, uint chunkLength)
        {
            var pos = Position - 4;
            var trns = new tRNS();
            var colorMode = state.InfoPng.ColorMode;
            if (colorMode.ColorType == PngColorType.Palette)
            {
                /*error: more alpha values given than there are palette entries*/
                if (chunkLength > colorMode.PaletteSize)
                {
                    state.Error = 39;
                }

                for (int i = 0; i != chunkLength; ++i)
                {
                    colorMode.Palette[4 * i + 3] = (byte)ReadByte();
                }
            }
            else if (colorMode.ColorType == PngColorType.Gray)
            {
                /*error: this chunk must be 2 bytes for grayscale image*/
                if (chunkLength != 2)
                {
                    state.Error = 30;
                }

                colorMode.IsKeyDefined = true;
                var keyValue = ReadUInt16();
                colorMode.KeyR = colorMode.KeyG = colorMode.KeyB = keyValue;
                trns.KeyR = trns.KeyG = trns.KeyB = keyValue;
            }
            else if (colorMode.ColorType == PngColorType.RGB)
            {
                /*error: this chunk must be 6 bytes for RGB image*/
                if (chunkLength != 6)
                {
                    state.Error = 41;
                }
                colorMode.IsKeyDefined = true;
                trns.KeyR = ReadUInt16();
                trns.KeyG = ReadUInt16();
                trns.KeyB = ReadUInt16();

                colorMode.KeyR = trns.KeyR;
                colorMode.KeyG = trns.KeyG;
                colorMode.KeyB = trns.KeyB;
            }
            else
            {
                /*error: tRNS chunk not allowed for other color models*/
                state.Error = 42;
            }

            trns.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            trns.CheckSum = Crc32.CalculateCheckSum(bytes);

            return trns;
        }

        internal PLTE ReadPLTE(PngState state, uint chunkLength)
        {
            var pos = Position - 4;
            var plte = new PLTE();
            var colorMode = state.InfoPng.ColorMode;
            //plte.PaletteSize = (int)(chunkLength / 3);
            var paletteSize = (int)(chunkLength / 3);
            plte.Palette = new byte[4 * paletteSize];

            if (plte.PaletteSize > 256)
            {
                /*error: palette too big*/
                state.Error = 38;
            }

            int index = 0;
            var data = ReadBytes((int)chunkLength);
            for (int i = 0; i != plte.PaletteSize; ++i)
            {
                plte.Palette[4 * i + 0] = data[index++]; // R
                plte.Palette[4 * i + 1] = data[index++]; // G
                plte.Palette[4 * i + 2] = data[index++]; // B
                plte.Palette[4 * i + 3] = 255; // aplha
            }

            state.InfoPng.ColorMode.PaletteSize = plte.PaletteSize;
            state.InfoPng.ColorMode.Palette = plte.Palette;

            plte.CRC = ReadUInt32();
            Position = pos;
            var bytes = ReadBytes(4 + (int)chunkLength);
            plte.CheckSum = Crc32.CalculateCheckSum(bytes);
            return plte;
        }

        internal acTL ReadacTL(PngState state)
        {
            var pos = Position - 4;
            var actl = new acTL();
            actl.FramesCount = ReadUInt32();
            actl.RepeatCout = ReadUInt32();

            state.InfoPng.FramesCount = actl.FramesCount;
            state.InfoPng.RepeatCount = actl.RepeatCout;

            ReadCRC(state, actl, pos, 12);
            return actl;
        }

        internal void ReadfcTL(PngState state, PngFrame frame)
        {
            var pos = Position - 4;
            var fctl = new fcTL();

            frame.SequenceNumberFCTL = ReadUInt32();
            frame.EncodedWidth = ReadUInt32();
            frame.EncodedHeight = ReadUInt32();
            frame.XOffset = ReadUInt32();
            frame.YOffset = ReadUInt32();
            frame.DelayNumerator = ReadUInt16();
            frame.DelayDenominator = ReadUInt16();
            frame.DisposeOp = (DisposeOp)ReadByte();
            frame.BlendOp = (BlendOp)ReadByte();

            ReadCRC(state, fctl, pos, 30);
        }

        internal void ReadCRC(PngState state, Chunk chunk, long position, uint sizeToRead)
        {
            chunk.CRC = ReadUInt32();
            Position = position;
            var bytes = ReadBytes((int)sizeToRead);
            chunk.CheckSum = Crc32.CalculateCheckSum(bytes);

            if (chunk.CRC != chunk.CheckSum)
            {
                state.Error = 57; // checksum mismatch;
            }
        }
    }
}
