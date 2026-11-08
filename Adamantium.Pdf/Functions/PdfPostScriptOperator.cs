namespace Adamantium.Pdf.Functions;

/// <summary>The operators of a type 4 function (ISO 32000-2, table 42), plus a pushed number and the two block calls.
/// </summary>
internal enum PdfPostScriptOperator
{
    Push,
    If,
    IfElse,
    Abs, Add, Atan, Ceiling, Cos, Cvi, Cvr, Div, Exp, Floor, Idiv, Ln, Log, Mod, Mul, Neg, Round, Sin, Sqrt, Sub,
    Truncate,
    And, Bitshift, Eq, False, Ge, Gt, Le, Lt, Ne, Not, Or, True, Xor,
    Copy, Dup, Exch, Index, Pop, Roll,
}
