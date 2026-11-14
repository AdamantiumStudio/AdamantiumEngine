namespace Adamantium.Pdf.Annotations;

/// <summary>The annotation subtypes marks are written as (ISO 32000-2, 12.5.6).</summary>
public static class PdfAnnotationTypes
{
    public const string Text = "Text";
    public const string FreeText = "FreeText";
    public const string Line = "Line";
    public const string Square = "Square";
    public const string Circle = "Circle";
    public const string Polygon = "Polygon";
    public const string PolyLine = "PolyLine";
    public const string Highlight = "Highlight";
    public const string Underline = "Underline";
    public const string Squiggly = "Squiggly";
    public const string StrikeOut = "StrikeOut";
    public const string Ink = "Ink";

    /// <summary>A picture shown by its appearance alone - nothing a viewer would draw again from other entries.</summary>
    public const string Stamp = "Stamp";
    public const string Link = "Link";
    public const string Widget = "Widget";
    public const string Popup = "Popup";
}
