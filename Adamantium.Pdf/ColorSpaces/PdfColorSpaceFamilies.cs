namespace Adamantium.Pdf.ColorSpaces;

/// <summary>The color space families (ISO 32000-2, 8.6.3), and the names of the spaces that stand in for the device
/// ones (8.6.5.6).</summary>
internal static class PdfColorSpaceFamilies
{
    public const string DeviceGray = "DeviceGray";
    public const string DeviceRGB = "DeviceRGB";
    public const string DeviceCMYK = "DeviceCMYK";
    public const string CalGray = "CalGray";
    public const string CalRGB = "CalRGB";
    public const string Lab = "Lab";
    public const string ICCBased = "ICCBased";
    public const string Indexed = "Indexed";
    public const string Pattern = "Pattern";
    public const string Separation = "Separation";
    public const string DeviceN = "DeviceN";

    public const string DefaultGray = "DefaultGray";
    public const string DefaultRGB = "DefaultRGB";
    public const string DefaultCMYK = "DefaultCMYK";
}
