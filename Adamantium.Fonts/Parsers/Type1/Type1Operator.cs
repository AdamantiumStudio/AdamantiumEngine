namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>The one-byte operators of a Type 1 charstring.</summary>
internal enum Type1Operator : byte
{
    HStem = 1,
    VStem = 3,
    VMoveTo = 4,
    RLineTo = 5,
    HLineTo = 6,
    VLineTo = 7,
    RRCurveTo = 8,
    ClosePath = 9,
    CallSubr = 10,
    Return = 11,
    Escape = 12,
    Hsbw = 13,
    EndChar = 14,
    RMoveTo = 21,
    HMoveTo = 22,
    VHCurveTo = 30,
    HVCurveTo = 31,
}
