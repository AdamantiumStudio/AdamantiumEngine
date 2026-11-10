namespace Adamantium.Pdf.Graphics;

/// <summary>The keys and names of graphics state parameter dictionaries, soft masks and transparency groups, as
/// ISO 32000-2 (8.4.5, 11.6.5, 11.6.6) names them.</summary>
internal static class PdfGraphicsKeys
{
    public const string LineWidth = "LW";
    public const string LineCap = "LC";
    public const string LineJoin = "LJ";
    public const string MiterLimit = "ML";
    public const string Dash = "D";
    public const string RenderingIntent = "RI";
    public const string Font = "Font";
    public const string BlendMode = "BM";
    public const string SoftMask = "SMask";
    public const string StrokeAlpha = "CA";
    public const string FillAlpha = "ca";
    public const string AlphaIsShape = "AIS";
    public const string TextKnockout = "TK";

    public const string Kind = "S";
    public const string Group = "G";
    public const string Backdrop = "BC";
    public const string Transfer = "TR";
    public const string Alpha = "Alpha";
    public const string Luminosity = "Luminosity";
    public const string None = "None";
    public const string Identity = "Identity";

    public const string FormGroup = "Group";
    public const string Transparency = "Transparency";
    public const string ColorSpace = "CS";
    public const string Isolated = "I";
    public const string Knockout = "K";
}
