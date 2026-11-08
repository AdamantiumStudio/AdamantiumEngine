namespace Adamantium.Pdf.Parsing;

/// <summary>What a token of PDF syntax is.</summary>
internal enum PdfTokenKind
{
    EndOfData,
    Integer,
    Real,
    LiteralString,
    HexString,
    Name,
    Keyword,
    ArrayStart,
    ArrayEnd,
    DictionaryStart,
    DictionaryEnd,
    ProcedureStart,
    ProcedureEnd,
}
