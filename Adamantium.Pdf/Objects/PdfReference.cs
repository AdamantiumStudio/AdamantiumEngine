namespace Adamantium.Pdf.Objects;

/// <summary>A reference to an indirect object, <c>N G R</c> in the file.</summary>
public sealed class PdfReference : PdfObject
{
    public PdfReference(PdfObjectId id)
    {
        Id = id;
    }

    public PdfReference(int number, int generation) : this(new PdfObjectId(number, generation))
    {
    }

    public PdfObjectId Id { get; }

    public override string ToString() => Id + " R";
}
