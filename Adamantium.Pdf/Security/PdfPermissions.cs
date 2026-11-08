using System;

namespace Adamantium.Pdf.Security;

/// <summary>What the user password allows (ISO 32000-2, table 22, <c>/P</c>). The owner password allows all.</summary>
[Flags]
public enum PdfPermissions
{
    None = 0,
    Print = 1 << 2,
    Modify = 1 << 3,
    CopyContent = 1 << 4,
    Annotate = 1 << 5,
    FillForms = 1 << 8,
    ExtractForAccessibility = 1 << 9,
    Assemble = 1 << 10,
    PrintHighQuality = 1 << 11,
    All = Print | Modify | CopyContent | Annotate | FillForms | ExtractForAccessibility | Assemble | PrintHighQuality,
}
