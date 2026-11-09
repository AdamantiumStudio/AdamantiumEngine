using System;
using System.IO;
using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Extensions
{
    internal static partial class FontStreamExtensions
    {
        public static FontStreamReader LoadIntoStream(this string file)
        {
            var bytes = File.ReadAllBytes(file);
            return new FontStreamReader(bytes, file);
        }

        public static FontStreamReader LoadIntoStream(this byte[] buffer)
        {
            return new(buffer);
        }

        public static UInt16[] ReadUInt16Array(this FontStreamReader reader, int count)
        {
            reader.EnsureAvailable(count, 2);
            var arr = new UInt16[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = reader.ReadUInt16();
            }

            return arr;
        }

        public static Int16[] ReadInt16Array(this FontStreamReader reader, int count)
        {
            reader.EnsureAvailable(count, 2);
            var arr = new Int16[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = reader.ReadInt16();
            }

            return arr;
        }

        public static UInt32[] ReadUInt24Array(this FontStreamReader reader, int count)
        {
            reader.EnsureAvailable(count, 3);
            var arr = new UInt32[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = reader.ReadUInt24();
            }

            return arr;
        }
        
        public static UInt32[] ReadUInt32Array(this FontStreamReader reader, int count)
        {
            reader.EnsureAvailable(count, 4);
            var arr = new UInt32[count];
            for (int i = 0; i < count; i++)
            {
                arr[i] = reader.ReadUInt32();
            }

            return arr;
        }

    }
}