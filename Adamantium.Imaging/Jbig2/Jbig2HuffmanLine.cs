namespace Adamantium.Imaging.Jbig2;

/// <summary>One line of a JBIG2 Huffman table (ITU-T T.88, B.2): the values from <see cref="RangeLow"/> on, their
/// prefix length and the bits of offset after the prefix. The lower range line counts down from its value.</summary>
internal readonly record struct Jbig2HuffmanLine(int PrefixLength, int RangeLength, int RangeLow, bool IsLower = false,
    bool IsOutOfBand = false)
{
    public static Jbig2HuffmanLine Lower(int prefixLength, int rangeLow) => new(prefixLength, 32, rangeLow, true);

    public static Jbig2HuffmanLine Upper(int prefixLength, int rangeLow) => new(prefixLength, 32, rangeLow);

    public static Jbig2HuffmanLine OutOfBand(int prefixLength) => new(prefixLength, 0, 0, false, true);
}
