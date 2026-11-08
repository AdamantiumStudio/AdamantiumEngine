using System;

namespace Adamantium.Pdf.Objects;

/// <summary>A name, held without its slash and with its #xx escapes undone.</summary>
public sealed class PdfName : PdfObject, IEquatable<PdfName>
{
    public PdfName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool Equals(PdfName other) => other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is PdfName name && Equals(name);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => "/" + Value;
}
