namespace Adamantium.Imaging.Jbig2;

/// <summary>The segment types of JBIG2 (ITU-T T.88, 7.3).</summary>
internal static class Jbig2SegmentTypes
{
    public const int SymbolDictionary = 0;
    public const int IntermediateTextRegion = 4;
    public const int ImmediateTextRegion = 6;
    public const int ImmediateLosslessTextRegion = 7;
    public const int PatternDictionary = 16;
    public const int IntermediateHalftoneRegion = 20;
    public const int ImmediateHalftoneRegion = 22;
    public const int ImmediateLosslessHalftoneRegion = 23;
    public const int IntermediateGenericRegion = 36;
    public const int ImmediateGenericRegion = 38;
    public const int ImmediateLosslessGenericRegion = 39;
    public const int IntermediateRefinementRegion = 40;
    public const int ImmediateRefinementRegion = 42;
    public const int ImmediateLosslessRefinementRegion = 43;
    public const int PageInformation = 48;
    public const int EndOfPage = 49;
    public const int EndOfStripe = 50;
    public const int EndOfFile = 51;
    public const int Profiles = 52;
    public const int Tables = 53;
    public const int Extension = 62;
}
