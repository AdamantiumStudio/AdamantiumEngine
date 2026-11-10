namespace Adamantium.Pdf.Graphics;

/// <summary>What of a soft mask's group lets drawing through (ISO 32000-2, 11.6.5.2).</summary>
public enum PdfSoftMaskKind
{
    /// <summary>The group's opacity.</summary>
    Alpha,

    /// <summary>The group's luminosity, drawn over its backdrop color.</summary>
    Luminosity,
}
