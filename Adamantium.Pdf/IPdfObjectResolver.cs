using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf;

/// <summary>What turns a reference into the object it stands for.</summary>
public interface IPdfObjectResolver
{
    /// <summary>The object a reference stands for - <see cref="PdfNull.Value"/> when it stands for nothing - or the
    /// value itself when it is not a reference.</summary>
    PdfObject Resolve(PdfObject value);
}
