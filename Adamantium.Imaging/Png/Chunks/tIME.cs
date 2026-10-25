using System;
using System.Collections.Generic;
using Adamantium.Core;

namespace Adamantium.Imaging.Png.Chunks
{
    internal class tIME : Chunk
    {
        public tIME()
        {
            Name = "tIME";
        }

        public ushort Year { get; set; }
        public byte Month { get; set; }
        public byte Day { get; set; }
        public byte Hour { get; set; }
        public byte Minute { get; set; }
        public byte Second { get; set; }

        internal override byte[] GetChunkBytes(PngState state)
        {
            var bytes = new List<byte>();
            bytes.AddRange(GetNameAsBytes());
            bytes.AddRange(Utilities.GetBytesWithReversedEndian(Year));
            bytes.Add(Month);
            bytes.Add(Day);
            bytes.Add(Hour);
            bytes.Add(Minute);
            bytes.Add(Second);

            return bytes.ToArray();
        }

        /// <summary>The time the image says it was last changed, in UTC as PNG keeps it: the one it was given, or now.
        /// </summary>
        internal static tIME FromState(PngState state)
        {
            if (state.InfoPng.Time != null)
            {
                return state.InfoPng.Time;
            }

            var time = new tIME();
            var date = DateTime.UtcNow;
            time.Year = (ushort)date.Year;
            time.Month = (byte)date.Month;
            time.Day = (byte)date.Day;
            time.Hour = (byte)date.Hour;
            time.Minute = (byte)date.Minute;
            time.Second = (byte)date.Second;
            return time;
        }
    }
}