namespace Adamantium.Imaging.Jbig2;

/// <summary>How a region's pixels combine with the pixels under them (ITU-T T.88, 6.4.5 and 7.4.1.5).</summary>
internal enum Jbig2Combination
{
    Or = 0,
    And = 1,
    Xor = 2,
    Xnor = 3,
    Replace = 4,
}
