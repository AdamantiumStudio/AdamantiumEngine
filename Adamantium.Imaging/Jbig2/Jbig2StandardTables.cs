namespace Adamantium.Imaging.Jbig2;

/// <summary>The standard Huffman tables B.1 to B.15 of ITU-T T.88, annex B.5: each line its prefix length, offset
/// bits and lowest value, then the lower range line, the upper range line and the out-of-band line it has.</summary>
internal static class Jbig2StandardTables
{
    private static readonly Jbig2HuffmanTable[] Tables =
    [
        new([
            new(1, 4, 0), new(2, 8, 16), new(3, 16, 272), Jbig2HuffmanLine.Upper(3, 65808),
        ]),
        new([
            new(1, 0, 0), new(2, 0, 1), new(3, 0, 2), new(4, 3, 3), new(5, 6, 11), Jbig2HuffmanLine.Upper(6, 75),
            Jbig2HuffmanLine.OutOfBand(6),
        ]),
        new([
            new(8, 8, -256), new(1, 0, 0), new(2, 0, 1), new(3, 0, 2), new(4, 3, 3), new(5, 6, 11),
            Jbig2HuffmanLine.Lower(8, -257), Jbig2HuffmanLine.Upper(7, 75), Jbig2HuffmanLine.OutOfBand(6),
        ]),
        new([
            new(1, 0, 1), new(2, 0, 2), new(3, 0, 3), new(4, 3, 4), new(5, 6, 12), Jbig2HuffmanLine.Upper(5, 76),
        ]),
        new([
            new(7, 8, -255), new(1, 0, 1), new(2, 0, 2), new(3, 0, 3), new(4, 3, 4), new(5, 6, 12),
            Jbig2HuffmanLine.Lower(7, -256), Jbig2HuffmanLine.Upper(6, 76),
        ]),
        new([
            new(5, 10, -2048), new(4, 9, -1024), new(4, 8, -512), new(4, 7, -256), new(5, 6, -128), new(5, 5, -64),
            new(4, 5, -32), new(2, 7, 0), new(3, 7, 128), new(3, 8, 256), new(4, 9, 512), new(4, 10, 1024),
            Jbig2HuffmanLine.Lower(6, -2049), Jbig2HuffmanLine.Upper(6, 2048),
        ]),
        new([
            new(4, 9, -1024), new(3, 8, -512), new(4, 7, -256), new(5, 6, -128), new(5, 5, -64), new(4, 5, -32),
            new(4, 5, 0), new(5, 5, 32), new(5, 6, 64), new(4, 7, 128), new(3, 8, 256), new(3, 9, 512),
            new(3, 10, 1024), Jbig2HuffmanLine.Lower(5, -1025), Jbig2HuffmanLine.Upper(5, 2048),
        ]),
        new([
            new(8, 3, -15), new(9, 1, -7), new(8, 1, -5), new(9, 0, -3), new(7, 0, -2), new(4, 0, -1), new(2, 1, 0),
            new(5, 0, 2), new(6, 0, 3), new(3, 4, 4), new(6, 1, 20), new(4, 4, 22), new(4, 5, 38), new(5, 6, 70),
            new(5, 7, 134), new(6, 7, 262), new(7, 8, 390), new(6, 10, 646), Jbig2HuffmanLine.Lower(9, -16),
            Jbig2HuffmanLine.Upper(9, 1670), Jbig2HuffmanLine.OutOfBand(2),
        ]),
        new([
            new(8, 4, -31), new(9, 2, -15), new(8, 2, -11), new(9, 1, -7), new(7, 1, -5), new(4, 1, -3),
            new(3, 1, -1), new(3, 1, 1), new(5, 1, 3), new(6, 1, 5), new(3, 5, 7), new(6, 2, 39), new(4, 5, 43),
            new(4, 6, 75), new(5, 7, 139), new(5, 8, 267), new(6, 8, 523), new(7, 9, 779), new(6, 11, 1291),
            Jbig2HuffmanLine.Lower(9, -32), Jbig2HuffmanLine.Upper(9, 3339), Jbig2HuffmanLine.OutOfBand(2),
        ]),
        new([
            new(7, 4, -21), new(8, 0, -5), new(7, 0, -4), new(5, 0, -3), new(2, 2, -2), new(5, 0, 2), new(6, 0, 3),
            new(7, 0, 4), new(8, 0, 5), new(2, 6, 6), new(5, 5, 70), new(6, 5, 102), new(6, 6, 134), new(6, 7, 198),
            new(6, 8, 326), new(6, 9, 582), new(6, 10, 1094), new(7, 11, 2118), Jbig2HuffmanLine.Lower(8, -22),
            Jbig2HuffmanLine.Upper(8, 4166), Jbig2HuffmanLine.OutOfBand(2),
        ]),
        new([
            new(1, 0, 1), new(2, 1, 2), new(4, 0, 4), new(4, 1, 5), new(5, 1, 7), new(5, 2, 9), new(6, 2, 13),
            new(7, 2, 17), new(7, 3, 21), new(7, 4, 29), new(7, 5, 45), new(7, 6, 77), Jbig2HuffmanLine.Upper(7, 141),
        ]),
        new([
            new(1, 0, 1), new(2, 0, 2), new(3, 1, 3), new(5, 0, 5), new(5, 1, 6), new(6, 1, 8), new(7, 0, 10),
            new(7, 1, 11), new(7, 2, 13), new(7, 3, 17), new(7, 4, 25), new(8, 5, 41), Jbig2HuffmanLine.Upper(8, 73),
        ]),
        new([
            new(1, 0, 1), new(3, 0, 2), new(4, 0, 3), new(5, 0, 4), new(4, 1, 5), new(3, 3, 7), new(6, 1, 15),
            new(6, 2, 17), new(6, 3, 21), new(6, 4, 29), new(6, 5, 45), new(7, 6, 77), Jbig2HuffmanLine.Upper(7, 141),
        ]),
        new([
            new(3, 0, -2), new(3, 0, -1), new(1, 0, 0), new(3, 0, 1), new(3, 0, 2),
        ]),
        new([
            new(7, 4, -24), new(6, 2, -8), new(5, 1, -4), new(4, 0, -2), new(3, 0, -1), new(1, 0, 0), new(3, 0, 1),
            new(4, 0, 2), new(5, 1, 3), new(6, 2, 5), new(7, 4, 9), Jbig2HuffmanLine.Lower(7, -25),
            Jbig2HuffmanLine.Upper(7, 25),
        ]),
    ];

    /// <summary>Table B.<paramref name="number"/>.</summary>
    public static Jbig2HuffmanTable Of(int number) => Tables[number - 1];
}
