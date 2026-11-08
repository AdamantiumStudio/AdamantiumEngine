using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Parsing;

/// <summary>An indirect object as read from the file: <c>N G obj ... endobj</c>.</summary>
internal readonly record struct PdfIndirectObject(PdfObjectId Id, PdfObject Value, int Start, int End);
