using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>The Huffman code of a text region's symbol numbers (ITU-T T.88, 7.4.3.1.7): run codes, then the prefix
/// length of each symbol in them.</summary>
internal static class Jbig2SymbolCodes
{
    private const int RunCodes = 35;

    public static Jbig2HuffmanTable Read(Jbig2BitReader bits, int symbols)
    {
        var runLines = new Jbig2HuffmanLine[RunCodes];
        for (var i = 0; i < RunCodes; i++)
        {
            runLines[i] = new Jbig2HuffmanLine((int)bits.ReadBits(4), 0, i);
        }

        var runs = new Jbig2HuffmanTable(runLines);
        var lengths = new int[symbols];
        var index = 0;
        while (index < symbols)
        {
            var code = runs.Decode(bits) ?? throw new InvalidDataException("A JBIG2 run code is out-of-band.");
            int length, repeat;
            switch (code)
            {
                case < 32:
                    (length, repeat) = (code, 1);
                    break;
                case 32:
                    if (index == 0)
                    {
                        throw new InvalidDataException("A JBIG2 symbol code repeats a length before the first.");
                    }

                    (length, repeat) = (lengths[index - 1], 3 + (int)bits.ReadBits(2));
                    break;
                case 33:
                    (length, repeat) = (0, 3 + (int)bits.ReadBits(3));
                    break;
                default:
                    (length, repeat) = (0, 11 + (int)bits.ReadBits(7));
                    break;
            }

            if (repeat > symbols - index)
            {
                throw new InvalidDataException("A JBIG2 symbol code has more lengths than symbols.");
            }

            for (var i = 0; i < repeat; i++)
            {
                lengths[index++] = length;
            }
        }

        bits.AlignToByte();
        var lines = new Jbig2HuffmanLine[symbols];
        for (var i = 0; i < symbols; i++)
        {
            lines[i] = new Jbig2HuffmanLine(lengths[i], 0, i);
        }

        return new Jbig2HuffmanTable(lines);
    }
}
