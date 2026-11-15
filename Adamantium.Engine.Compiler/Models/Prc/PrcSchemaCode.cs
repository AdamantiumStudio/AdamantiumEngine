namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>What a schema instruction does.</summary>
internal enum PrcSchemaCode
{
    Read,
    ReadValue,
    Constant,
    Variable,
    Declare,
    Store,
    Discard,
    LoopIndex,
    Binary,
    JumpIfFalse,
    Jump,
    LoopStart,
    CountedLoopStart,
    LoopTest,
    LoopNext,
    Version,
    Entity,
    Unsupported
}
