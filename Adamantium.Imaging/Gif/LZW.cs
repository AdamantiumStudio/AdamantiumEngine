using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Gif
{
    internal static class LZW
    {
        private static int EOF = -1;
        private static int MAXBITS = 12;
        private static int HSIZE = 5003; // 80% occupancy
        private static int[] masks = {0x0000, 0x0001, 0x0003, 0x0007, 0x000F, 0x001F,
                     0x003F, 0x007F, 0x00FF, 0x01FF, 0x03FF, 0x07FF,
                     0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF };

        public static byte[] Compress(int[] indexData, int colorDepth)
        {
            var outData = new List<byte>();
            int[] htab = new int[HSIZE];
            int[] codetab = new int[HSIZE];
            int hsize = HSIZE; // for dynamic table sizing
            int maxbits = MAXBITS;
            int maxmaxcode = 1 << MAXBITS; // should NEVER generate this code

            int initCodeSize = Math.Max(2, colorDepth) + 1;
            int remaining = indexData.Length;
            int curPixel = 0;

            int cur_accum = 0;
            int cur_bits = 0;

            int fcode;
            int i /* = 0 */;
            int c;
            int disp;
            int hsize_reg;
            int hshift;

            //g_init_bits - initial number of bits
            int g_init_bits = initCodeSize;

            bool clear_flg = false;
            int n_bits = g_init_bits;
            int maxcode = GetMaxCode(n_bits);

            int ClearCode = 1 << colorDepth;
            int EOFCode = ClearCode + 1;
            int free_ent = ClearCode + 2; // first unused entry

            int GetMaxCode(int bits)
            {
                return (1 << bits) - 1;
            }

            void ClearHash(int size)
            {
                for (var i = 0; i < size; ++i) htab[i] = -1;
            }

            int GetNextPixel()
            {
                if (remaining == 0) return EOF;
                --remaining;
                var pixel = indexData[curPixel++];
                return pixel & 0xff;
            }

            void Output(int code, List<byte> outs)
            {
                cur_accum &= masks[cur_bits];

                if (cur_bits > 0)
                {
                    cur_accum |= code << cur_bits;
                }
                else
                {
                    cur_accum = code;
                }

                cur_bits += n_bits;

                while (cur_bits >= 8)
                {
                    outs.Add((byte)(cur_accum & 0xff));
                    cur_accum >>= 8;
                    cur_bits -= 8;
                }

                // If the next entry is going to be too big for the code size,
                // then increase it, if possible.
                if (free_ent > maxcode || clear_flg)
                {
                    if (clear_flg)
                    {
                        maxcode = GetMaxCode(n_bits = g_init_bits);
                        clear_flg = false;
                    }
                    else
                    {
                        ++n_bits;
                        if (n_bits == maxbits)
                        {
                            maxcode = maxmaxcode;
                        }
                        else
                        {
                            maxcode = GetMaxCode(n_bits);
                        }
                    }
                }

                if (code == EOFCode)
                {
                    // At EOF, write the rest of the buffer.
                    while (cur_bits > 0)
                    {
                        outs.Add((byte)(cur_accum & 0xff));
                        cur_accum >>= 8;
                        cur_bits -= 8;
                    }
                }
            }

            void ClearBlock(List<byte> outs)
            {
                ClearHash(hsize);
                free_ent = ClearCode + 2;
                clear_flg = true;

                Output(ClearCode, outs);
            }

            var ent = GetNextPixel();
            hshift = 0;
            for (fcode = hsize; fcode < 65536; fcode *= 2)
            {
                ++hshift;
            }
            hshift = 8 - hshift; // set hash code range bound
            hsize_reg = hsize;
            ClearHash(hsize_reg);

            Output(ClearCode, outData);

        outerLoop:
            while ((c = GetNextPixel()) != EOF)
            {
                fcode = (c << maxbits) + ent;
                i = (c << hshift) ^ ent; // xor hashing

                if (htab[i] == fcode)
                {
                    ent = codetab[i];
                    continue;
                }
                else if (htab[i] >= 0) // non-empty slot
                {
                    disp = hsize_reg - i; // secondary hash (after G. Knott)
                    if (i == 0)
                    {
                        disp = 1;
                    }
                    do
                    {
                        if ((i -= disp) < 0)
                        {
                            i += hsize_reg;
                        }

                        if (htab[i] == fcode)
                        {
                            ent = codetab[i];
                            goto outerLoop;
                        }
                    }
                    while (htab[i] >= 0);
                }
                Output(ent, outData);
                ent = c;
                if (free_ent < maxmaxcode)
                {
                    codetab[i] = free_ent++; // code -> hashtable
                    htab[i] = fcode;
                }
                else
                {
                    ClearBlock(outData);
                }
            }

            Output(ent, outData);
            Output(EOFCode, outData);

            return outData.ToArray();
        }

        public static int[] Decompress(ReadOnlySpan<byte> compressedData, int minimumCodeSize, int pixelCount)
        {
            const int maxCodes = 4096;
            var output = new int[pixelCount];
            Span<short> prefix = stackalloc short[maxCodes];
            Span<byte> suffix = stackalloc byte[maxCodes];
            Span<byte> stack = stackalloc byte[maxCodes + 1];

            var clearCode = 1 << minimumCodeSize;
            var eoiCode = clearCode + 1;
            for (var i = 0; i < clearCode; i++)
            {
                suffix[i] = (byte)i;
            }

            var codeSize = minimumCodeSize + 1;
            var available = clearCode + 2;
            var previous = -1;
            var first = 0;
            var datum = 0;
            var bits = 0;
            var position = 0;
            var written = 0;

            while (written < pixelCount)
            {
                while (bits < codeSize)
                {
                    if (position >= compressedData.Length) return output;
                    datum |= compressedData[position++] << bits;
                    bits += 8;
                }

                var code = datum & ((1 << codeSize) - 1);
                datum >>= codeSize;
                bits -= codeSize;

                if (code == clearCode)
                {
                    codeSize = minimumCodeSize + 1;
                    available = clearCode + 2;
                    previous = -1;
                    continue;
                }

                if (code == eoiCode) break;

                var top = 0;
                if (previous == -1)
                {
                    if (code >= clearCode) break;
                    first = code;
                    stack[top++] = (byte)code;
                }
                else
                {
                    var entry = code;
                    if (entry >= available)
                    {
                        if (entry > available) break;
                        stack[top++] = (byte)first;
                        entry = previous;
                    }

                    while (entry >= clearCode)
                    {
                        stack[top++] = suffix[entry];
                        entry = prefix[entry];
                    }

                    first = suffix[entry];
                    stack[top++] = (byte)first;

                    if (available < maxCodes)
                    {
                        prefix[available] = (short)previous;
                        suffix[available] = (byte)first;
                        available++;
                    }
                }

                while (top > 0 && written < pixelCount)
                {
                    output[written++] = stack[--top];
                }

                previous = code;
                if (available == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            return output;
        }
    }
}
