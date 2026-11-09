namespace Adamantium.Imaging.Jbig2;

/// <summary>Where the pixels of a generic region template sit in its context: the fixed pixels two rows up and one
/// row up (leftmost x relative to the pixel, count, lowest bit), the count to the left on the row (the lowest bits),
/// the bits of the adaptive pixels, and the context of typical prediction.</summary>
internal sealed record Jbig2GenericTemplate(int Up2Left, int Up2Count, int Up2Shift, int Up1Left, int Up1Count,
    int Up1Shift, int LeftCount, int[] AtBits, int TypicalContext);
