namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>The operators of a Type 1 charstring that follow the escape byte 12.</summary>
internal enum Type1EscapeOperator : byte
{
    DotSection = 0,
    VStem3 = 1,
    HStem3 = 2,
    Seac = 6,
    Sbw = 7,
    Div = 12,
    CallOtherSubr = 16,
    Pop = 17,
    SetCurrentPoint = 33,
}
