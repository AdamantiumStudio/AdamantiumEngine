namespace Adamantium.Imaging.Icc;

/// <summary>The tag signatures a device-to-PCS transform reads (ICC.1, 9.2).</summary>
internal static class IccTags
{
    public const uint AToB0 = 0x41324230;
    public const uint AToB1 = 0x41324231;
    public const uint AToB2 = 0x41324232;
    public const uint BToA0 = 0x42324130;
    public const uint RedColorant = 0x7258595A;
    public const uint GreenColorant = 0x6758595A;
    public const uint BlueColorant = 0x6258595A;
    public const uint RedTrc = 0x72545243;
    public const uint GreenTrc = 0x67545243;
    public const uint BlueTrc = 0x62545243;
    public const uint GrayTrc = 0x6B545243;
    public const uint MediaWhitePoint = 0x77747074;
}
