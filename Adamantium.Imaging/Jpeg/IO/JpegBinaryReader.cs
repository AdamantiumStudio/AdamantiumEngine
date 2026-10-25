/// Copyright (c) 2008 Jeffrey Powers for Fluxcapacity Open Source.
/// Under the MIT License, details: License.txt.

using System.IO;

namespace Adamantium.Imaging.Jpeg.IO
{
    internal class JPEGBinaryReader : BinaryReader
    {
        public int eob_run = 0;

        private byte marker;

        public JPEGBinaryReader(Stream input)
            : base(input)
        {
        }

        /// <summary>
        /// Seeks through the stream until a marker is found.
        /// </summary>
        public byte GetNextMarker()
        {
            try { while (true) { ReadJpegByte(); } }
            catch (JpegMarkerFoundException ex)
            {
                return ex.Marker;
            }
        }

        // THE BIT BUFFER, up to 32 bits of it, most-significant first.
        //
        // It used to hold a single byte, which forced every consumer to ask for one bit at a time - a Huffman code was
        // decoded by calling ReadBits(1) once per bit, and a 4K photograph needs tens of millions of them. Holding
        // several bytes lets a decoder LOOK at the next few bits without consuming them, which is what makes a
        // table-driven Huffman decode possible (see HuffmanTable.Decode).
        uint _bitBuffer;

        protected int _bitsLeft = 0;

        /// <summary>Bits available without touching the stream. A decoder peeks at most this many.</summary>
        public int BitsBuffered => _bitsLeft;

        /// <summary>
        /// Buffer ahead until <paramref name="count"/> bits are available, and return how many actually are.
        ///
        /// <para>STOPS BEFORE A MARKER, leaving it in the stream. That is the whole difficulty of reading ahead in a
        /// JPEG: a marker ends the entropy-coded segment and the decoder above expects to find it still there, so
        /// consuming one early - which <see cref="ReadJpegByte"/> does, by design, since it cannot un-read it - loses
        /// the end of the scan. Read ahead peeks at the stream instead, and puts back anything it must not take.</para>
        ///
        /// <para>Returns what is buffered without reaching for more when the stream cannot seek, or when the next byte
        /// begins a marker. The caller then falls back to reading bit by bit, which handles the boundary as it always
        /// did.</para>
        /// </summary>
        public int FillBits(int count)
        {
            var stream = BaseStream;
            if (!stream.CanSeek) return _bitsLeft;

            while (_bitsLeft < count && _bitsLeft <= 24)
            {
                var mark = stream.Position;
                var read = stream.ReadByte();
                if (read < 0) break;                       // end of the data

                if (read == JPEGMarker.XFF)
                {
                    // Padding runs of 0xFF are skipped exactly as ReadJpegByte skips them...
                    int following;
                    while ((following = stream.ReadByte()) == JPEGMarker.XFF) { }

                    // ...0xFF00 is an escaped 0xFF byte...
                    if (following == 0)
                    {
                        read = JPEGMarker.XFF;
                    }
                    else
                    {
                        // ...and anything else is a MARKER: put the stream back where it was and stop. The decoder
                        // reads it through its own path, in its own time.
                        stream.Position = mark;
                        break;
                    }
                }

                _bitBuffer = (_bitBuffer << 8) | (uint)read;
                _bitsLeft += 8;
            }

            return _bitsLeft;
        }

        /// <summary>The next <paramref name="n"/> bits WITHOUT consuming them. Requires that many to be buffered.</summary>
        public int PeekBits(int n) => (int)((_bitBuffer >> (_bitsLeft - n)) & ((1u << n) - 1));

        /// <summary>Throw away <paramref name="n"/> bits that were peeked at.</summary>
        public void DropBits(int n) => _bitsLeft -= n;

        /// <summary>Throws away the bits still buffered: the padding that ends an interval or a scan.</summary>
        public void DiscardBits() => _bitsLeft = 0;

        /// <summary>One bit. Written out rather than routed through <see cref="ReadBits"/> because the general path
        /// costs a loop and a handful of branches, and this is called for every bit of every Huffman code that the
        /// look-ahead table does not resolve.</summary>
        public int ReadBit()
        {
            if (_bitsLeft == 0)
            {
                _bitBuffer = ReadJpegByte();
                _bitsLeft = 8;
            }

            _bitsLeft--;
            return (int)((_bitBuffer >> _bitsLeft) & 1);
        }

        /// <summary>
        /// Places n bits from the stream, where the most-significant bits
        /// from the first byte read end up as the most-significant of the returned
        /// n bits.
        /// </summary>
        /// <param name="n">Number of bits to return</param>
        /// <returns>Integer containing the bits desired -- shifted all the way right.</returns>
        public int ReadBits(int n)
        {
            if (n == 0) return 0;

            // Already buffered - take them off the top in one step.
            if (_bitsLeft >= n)
            {
                _bitsLeft -= n;
                return (int)((_bitBuffer >> _bitsLeft) & ((1u << n) - 1));
            }

            int result = 0;
            while (n > 0)
            {
                if (_bitsLeft == 0)
                {
                    _bitBuffer = ReadJpegByte();
                    _bitsLeft = 8;
                }

                int take = n <= _bitsLeft ? n : _bitsLeft;
                _bitsLeft -= take;
                n -= take;
                result |= (int)(((_bitBuffer >> _bitsLeft) & ((1u << take) - 1)) << n);
            }

            return result;
        }

        protected byte ReadJpegByte()
        {
            byte c = ReadByte();

            /* If it's 0xFF, check and discard stuffed zero byte */
            if (c == JPEGMarker.XFF)
            {
                // Discard padded oxFFs
                while ((c = ReadByte()) == 0xff) ;

                // ff00 is the escaped form of 0xff
                if (c == 0) c = 0xff;
                else
                {
                    // Otherwise we've found a new marker.
                    marker = c;
                    throw new JpegMarkerFoundException(marker);
                }
            }

            return c;
        }

    }
}
