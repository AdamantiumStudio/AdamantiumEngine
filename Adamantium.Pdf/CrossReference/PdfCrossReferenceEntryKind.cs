namespace Adamantium.Pdf.CrossReference;

/// <summary>Where an object is, according to the cross-reference.</summary>
public enum PdfCrossReferenceEntryKind
{
    /// <summary>Not in use.</summary>
    Free,

    /// <summary>At a byte offset of the file.</summary>
    InFile,

    /// <summary>Inside an object stream.</summary>
    InObjectStream,
}
