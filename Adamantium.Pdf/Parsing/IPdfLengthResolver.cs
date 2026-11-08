using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Parsing;

/// <summary>What resolves a stream's <c>/Length</c> when it is a reference, while the stream is being read.</summary>
internal interface IPdfLengthResolver
{
    /// <summary>The integer the reference stands for, or null when it cannot be had - unknown, or being read already.
    /// </summary>
    long? ResolveLength(PdfReference reference);
}
