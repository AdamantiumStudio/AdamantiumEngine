using System.Globalization;

namespace Adamantium.Pdf.Objects;

/// <summary>The object number and generation of an indirect object.</summary>
public readonly record struct PdfObjectId(int Number, int Generation)
{
    public override string ToString() =>
        Number.ToString(CultureInfo.InvariantCulture) + " " + Generation.ToString(CultureInfo.InvariantCulture);
}
