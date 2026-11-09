using System;
using System.IO;
using System.Text;

namespace Adamantium.Fonts.Common
{
    public class FontStreamReader : MemoryStream
    {
        public string FilePath { get; }
        
        public FontStreamReader()
        {
        }
        
        public FontStreamReader(byte[] buffer, string path = "") : base(buffer, 0, buffer.Length, false, true)
        {
            FilePath = path;
        }

        /// <exception cref="InvalidDataException">An offset the font gives lies before its data or beyond any stream.
        /// </exception>
        public override long Position
        {
            get => base.Position;
            set
            {
                if (value < 0 || value > int.MaxValue)
                {
                    throw new InvalidDataException("An offset in the font lies outside its data.");
                }

                base.Position = value;
            }
        }
        
        public byte[] ReadBytes(long count, bool ignoreEndian = false)
        {
            EnsureAvailable(count);
            byte[] bytes = new byte[count];
            for (int i = 0; i < count; ++i)
            {
                bytes[i] = ReadByte();
            }

            if (BitConverter.IsLittleEndian && !ignoreEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }
        
        public sbyte[] ReadSignedBytes(long count)
        {
            EnsureAvailable(count);
            sbyte[] bytes = new sbyte[count];
            for (int i = 0; i < count; ++i)
            {
                bytes[i] = ReadSignedByte();
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public sbyte ReadSignedByte()
        {
            return (sbyte)ReadByte();
        }

        /// <exception cref="EndOfStreamException">The font data ends before the byte.</exception>
        public new byte ReadByte()
        {
            var value = base.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException("The font data ends early.");
            }

            return (byte)value;
        }

        /// <summary>Fails before anything is allocated for <paramref name="count"/> values of
        /// <paramref name="size"/> bytes that the data does not hold.</summary>
        /// <exception cref="EndOfStreamException">The font data ends before them.</exception>
        internal void EnsureAvailable(long count, int size = 1)
        {
            if (count < 0 || count > (Length - Position) / size)
            {
                throw new EndOfStreamException("The font data ends early.");
            }
        }

        public UInt16 ReadUInt16()
        {
            var high = ReadByte();
            var low = ReadByte();
            return (UInt16)((high << 8) | low);
        }

        public UInt32 ReadUInt24()
        {
            var high = ReadByte();
            var middle = ReadByte();
            var low = ReadByte();
            return (UInt32)((high << 16) | (middle << 8) | low);
        }

        public UInt32 ReadUInt32()
        {
            var high = ReadUInt16();
            var low = ReadUInt16();
            return ((UInt32)high << 16) | low;
        }

        public UInt64 ReadUInt64()
        {
            var high = ReadUInt32();
            var low = ReadUInt32();
            return ((UInt64)high << 32) | low;
        }

        public Int16 ReadInt16()
        {
            return (Int16)ReadUInt16();
        }

        public Int32 ReadInt32()
        {
            return (Int32)ReadUInt32();
        }

        public Int64 ReadInt64()
        {
            return (Int64)ReadUInt64();
        }

        public Single ReadFloat()
        {
            var bytes = ReadBytes(sizeof(Single));
            return BitConverter.ToSingle(bytes, 0);
        }

        public String ReadString(int length)
        {
            var bytes = ReadBytes(length);
            Array.Reverse(bytes);
            return Encoding.ASCII.GetString(bytes, 0, bytes.Length);
        }
        
        public String ReadString(int length, Encoding encoding)
        {
            var bytes = ReadBytes(length);
            Array.Reverse(bytes);
            return encoding.GetString(bytes, 0, bytes.Length);
        }
    }
}
