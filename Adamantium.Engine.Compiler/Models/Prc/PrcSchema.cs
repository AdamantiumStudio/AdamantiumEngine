using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The schema a file structure carries at the start of its globals, and the model file at the start of its
/// section (PRC "Serialize Schema", "Schema processing"): for each entity type that changed between the version the file
/// asks of its reader and the one it was written by, the tokens that read what was added. A reader reads what it knows
/// of an entity, then runs the versioned blocks newer than itself to step over the rest; an entity of a type it does not
/// know at all it steps over by running all of its tokens. The tokens are compiled into instructions once, and run by a
/// loop with stacks of its own - no recursion, however deep the data nests.</summary>
internal sealed class PrcSchema
{
    /// <summary>The version of the specification this reader reads natively; versioned blocks up to it are its own.</summary>
    internal const uint ReaderVersion = 8137;

    private const int MaximumCallDepth = 64;
    private const long MaximumSteps = 50_000_000;

    private readonly Dictionary<uint, PrcSchemaProgram> programs = new();

    /// <summary>An empty schema: nothing to step over.</summary>
    internal static PrcSchema Empty { get; } = new();

    /// <summary>The entity types the schema describes.</summary>
    internal IReadOnlyCollection<uint> Types => programs.Keys;

    /// <summary>Reads a schema: its count of entity types, then for each its type, its count of tokens and the tokens.</summary>
    internal static PrcSchema Read(PrcBitReader reader)
    {
        var schema = new PrcSchema();
        var count = reader.ReadCount(2, "schema definitions");
        for (var index = 0; index < count; index++)
        {
            var type = reader.ReadUnsignedInteger();
            var tokenCount = reader.ReadCount(1, $"schema tokens of type {type}");
            var tokens = new uint[tokenCount];
            for (var token = 0; token < tokenCount; token++)
            {
                tokens[token] = reader.ReadUnsignedInteger();
            }

            schema.programs[type] = PrcSchemaProgram.Compile(type, tokens, reader);
        }

        return schema;
    }

    /// <summary>Whether the schema says how to read entities of <paramref name="type"/>.</summary>
    internal bool Describes(uint type) => programs.ContainsKey(type);

    /// <summary>Steps over what versions newer than this reader added to an entity of <paramref name="type"/>, whose
    /// own data has just been read.</summary>
    internal void SkipNewData(PrcBitReader reader, uint type)
    {
        if (programs.TryGetValue(type, out var program))
        {
            Run(reader, program, false);
        }
    }

    /// <summary>Steps over a whole entity of <paramref name="type"/>, a type this reader does not know; its type code
    /// has been read. Throws when the schema does not describe the type: nothing then says how long the entity is.</summary>
    internal void SkipEntity(PrcBitReader reader, uint type)
    {
        if (!programs.TryGetValue(type, out var program))
        {
            throw reader.Corrupt($"it holds an entity of type {type}, which neither this reader nor the file's schema knows");
        }

        Run(reader, program, true);
    }

    private void Run(PrcBitReader reader, PrcSchemaProgram first, bool wholeEntity)
    {
        var calls = new Stack<PrcSchemaCall>();
        var values = new Stack<double>();
        calls.Push(new PrcSchemaCall(first, wholeEntity));
        var steps = 0L;
        while (calls.Count > 0)
        {
            var call = calls.Peek();
            if (call.Position >= call.Program.Instructions.Count)
            {
                calls.Pop();
                continue;
            }

            if (++steps > MaximumSteps)
            {
                throw reader.Corrupt($"the schema of type {first.Type} runs past {MaximumSteps} steps");
            }

            var instruction = call.Program.Instructions[call.Position++];
            switch (instruction.Code)
            {
                case PrcSchemaCode.Read:
                    ReadValue(reader, (int)instruction.Operand);
                    break;
                case PrcSchemaCode.ReadValue:
                    values.Push(ReadValue(reader, (int)instruction.Operand));
                    break;
                case PrcSchemaCode.Constant:
                    values.Push(instruction.Operand);
                    break;
                case PrcSchemaCode.Variable:
                    values.Push(call.Variables.GetValueOrDefault((long)instruction.Operand));
                    break;
                case PrcSchemaCode.Declare:
                    call.Variables[(long)instruction.Operand] = 0;
                    break;
                case PrcSchemaCode.Store:
                    call.Variables[(long)instruction.Operand] = Pop(values, reader);
                    break;
                case PrcSchemaCode.Discard:
                    Pop(values, reader);
                    break;
                case PrcSchemaCode.LoopIndex:
                    values.Push(call.Loops.Count > 0 ? call.Loops.Peek().Index : 0);
                    break;
                case PrcSchemaCode.Binary:
                    var right = Pop(values, reader);
                    var left = Pop(values, reader);
                    values.Push(Apply((int)instruction.Operand, left, right, reader));
                    break;
                case PrcSchemaCode.JumpIfFalse:
                    if (Pop(values, reader) == 0)
                    {
                        call.Position = instruction.Target;
                    }

                    break;
                case PrcSchemaCode.Jump:
                    call.Position = instruction.Target;
                    break;
                case PrcSchemaCode.LoopStart:
                    call.Loops.Push(new PrcSchemaLoop(LoopCount(Pop(values, reader), reader)));
                    break;
                case PrcSchemaCode.CountedLoopStart:
                    call.Loops.Push(new PrcSchemaLoop(reader.ReadUnsignedInteger()));
                    break;
                case PrcSchemaCode.LoopTest:
                    if (call.Loops.Peek().Index >= call.Loops.Peek().Count)
                    {
                        call.Loops.Pop();
                        call.Position = instruction.Target;
                    }

                    break;
                case PrcSchemaCode.LoopNext:
                    call.Loops.Peek().Index++;
                    call.Position = instruction.Target;
                    break;
                case PrcSchemaCode.Version:
                    if (!call.WholeEntity && instruction.Operand <= ReaderVersion)
                    {
                        call.Position = instruction.Target;
                    }

                    break;
                case PrcSchemaCode.Entity:
                    var type = (uint)instruction.Operand;
                    if (!programs.TryGetValue(type, out var entity))
                    {
                        throw reader.Corrupt($"the schema of type {call.Program.Type} reads an entity of type {type}, " +
                                             "which only a schema of the file could say how to read and none does");
                    }

                    if (calls.Count >= MaximumCallDepth)
                    {
                        throw reader.Corrupt($"the schema entities nest more than {MaximumCallDepth} deep");
                    }

                    calls.Push(new PrcSchemaCall(entity, true));
                    break;
                case PrcSchemaCode.Unsupported:
                    throw reader.Corrupt($"the schema of type {call.Program.Type} reads a {Describe((int)instruction.Operand)}, " +
                                         "which this reader cannot step over");
            }
        }
    }

    private static double ReadValue(PrcBitReader reader, int kind)
    {
        switch (kind)
        {
            case PrcSchemaProgram.Boolean:
                return reader.ReadBoolean() ? 1 : 0;
            case PrcSchemaProgram.Double:
                return reader.ReadDouble();
            case PrcSchemaProgram.Character:
                return reader.ReadCharacter();
            case PrcSchemaProgram.UnsignedInteger:
                return reader.ReadUnsignedInteger();
            case PrcSchemaProgram.Integer:
                return reader.ReadInteger();
            case PrcSchemaProgram.String:
                return reader.ReadString()?.Length ?? 0;
            case PrcSchemaProgram.Vector2D:
                return ReadDoubles(reader, 2);
            case PrcSchemaProgram.Vector3D:
                return ReadDoubles(reader, 3);
            case PrcSchemaProgram.Extent1D:
                return ReadDoubles(reader, 2);
            case PrcSchemaProgram.Extent2D:
                return ReadDoubles(reader, 4);
            case PrcSchemaProgram.Extent3D:
                return ReadDoubles(reader, 6);
            default:
                throw reader.Corrupt($"the schema reads a value of kind {kind}");
        }
    }

    private static double ReadDoubles(PrcBitReader reader, int count)
    {
        for (var index = 0; index < count; index++)
        {
            reader.ReadDouble();
        }

        return 0;
    }

    private static double Pop(Stack<double> values, PrcBitReader reader)
    {
        if (values.Count == 0)
        {
            throw reader.Corrupt("a schema expression lacks a value");
        }

        return values.Pop();
    }

    private static uint LoopCount(double count, PrcBitReader reader)
    {
        if (count < 0 || count > uint.MaxValue || double.IsNaN(count))
        {
            throw reader.Corrupt($"a schema loop runs {count} times");
        }

        var whole = (uint)count;
        reader.CheckCount(whole, 1, "schema loop turns");
        return whole;
    }

    private static double Apply(int token, double left, double right, PrcBitReader reader) => token switch
    {
        PrcSchemaProgram.Multiply => left * right,
        PrcSchemaProgram.Divide => right == 0 ? throw reader.Corrupt("a schema expression divides by zero") : Math.Truncate(left / right),
        PrcSchemaProgram.Add => left + right,
        PrcSchemaProgram.Subtract => left - right,
        PrcSchemaProgram.Less => left < right ? 1 : 0,
        PrcSchemaProgram.LessOrEqual => left <= right ? 1 : 0,
        PrcSchemaProgram.Greater => left > right ? 1 : 0,
        PrcSchemaProgram.GreaterOrEqual => left >= right ? 1 : 0,
        PrcSchemaProgram.Equal => left == right ? 1 : 0,
        PrcSchemaProgram.NotEqual => left != right ? 1 : 0,
        _ => throw reader.Corrupt($"schema operator {token} is not known")
    };

    private static string Describe(int token) => token switch
    {
        PrcSchemaProgram.SurfacePointer => "surface",
        PrcSchemaProgram.CurvePointer => "curve",
        _ => $"token {token}"
    };
}

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

/// <summary>One compiled schema instruction: its code, a value it carries and the instruction it may go to.</summary>
internal readonly struct PrcSchemaInstruction
{
    internal PrcSchemaInstruction(PrcSchemaCode code, double operand, int target = -1)
    {
        Code = code;
        Operand = operand;
        Target = target;
    }

    internal PrcSchemaCode Code { get; }

    internal double Operand { get; }

    internal int Target { get; }

    internal PrcSchemaInstruction WithTarget(int target) => new(Code, Operand, target);
}

/// <summary>A run of one entity type's instructions: where it is, its values and its loops.</summary>
internal sealed class PrcSchemaCall
{
    internal PrcSchemaCall(PrcSchemaProgram program, bool wholeEntity)
    {
        Program = program;
        WholeEntity = wholeEntity;
    }

    internal PrcSchemaProgram Program { get; }

    /// <summary>True when the entity is read whole from the schema, false when only blocks newer than the reader run.</summary>
    internal bool WholeEntity { get; }

    internal int Position { get; set; }

    internal Dictionary<long, double> Variables { get; } = new();

    internal Stack<PrcSchemaLoop> Loops { get; } = new();
}

/// <summary>A schema loop under way.</summary>
internal sealed class PrcSchemaLoop
{
    internal PrcSchemaLoop(uint count)
    {
        Count = count;
    }

    internal uint Count { get; }

    internal uint Index { get; set; }
}

/// <summary>An open construct of the token stream while it compiles: what it is, how far it got and the instructions
/// waiting for a target.</summary>
internal sealed class PrcSchemaConstruct
{
    internal PrcSchemaConstruct(int kind, double operand)
    {
        Kind = kind;
        Operand = operand;
    }

    internal int Kind { get; }

    internal double Operand { get; }

    internal int Stage { get; set; }

    internal int FirstPending { get; set; } = -1;

    internal int SecondPending { get; set; } = -1;

    internal int LoopTop { get; set; } = -1;
}

/// <summary>One entity type's schema tokens compiled into instructions. The tokens are prefix code - an operator before
/// its operands, a loop before its count and its body - so each construct is kept open on a stack until its last part
/// has been compiled, and the jumps it needs are filled in then. The token values are those the files carry: the
/// specification's list numbers two tokens 18 and gives 6 as the father type, while files write 6 before an entity type
/// to read an entity of it, as the list's pointer to a typed object reads, and 39 before the father type, which reads
/// nothing in the file. A simple for loop reads its count, an UnsignedInteger, from the file.</summary>
internal sealed class PrcSchemaProgram
{
    internal const int Boolean = 0;
    internal const int Double = 1;
    internal const int Character = 2;
    internal const int UnsignedInteger = 3;
    internal const int Integer = 4;
    internal const int String = 5;
    internal const int TypedEntity = 6;
    internal const int Vector2D = 7;
    internal const int Vector3D = 8;
    internal const int Extent1D = 9;
    internal const int Extent2D = 10;
    internal const int Extent3D = 11;
    internal const int PointerToType = 12;
    internal const int SurfacePointer = 13;
    internal const int CurvePointer = 14;
    internal const int For = 15;
    internal const int SimpleFor = 16;
    internal const int If = 17;
    internal const int Else = 18;
    internal const int BlockStart = 19;
    internal const int BlockVersion = 20;
    internal const int BlockEnd = 21;
    internal const int ValueDeclare = 22;
    internal const int ValueSet = 23;
    internal const int ValueDeclareAndSet = 24;
    internal const int Value = 25;
    internal const int ValueConstant = 26;
    internal const int ValueFor = 27;
    internal const int CurveIs3D = 28;
    internal const int Multiply = 29;
    internal const int Divide = 30;
    internal const int Add = 31;
    internal const int Subtract = 32;
    internal const int Less = 33;
    internal const int LessOrEqual = 34;
    internal const int Greater = 35;
    internal const int GreaterOrEqual = 36;
    internal const int Equal = 37;
    internal const int NotEqual = 38;
    internal const int FatherType = 39;

    private readonly List<PrcSchemaInstruction> instructions = [];

    private PrcSchemaProgram(uint type)
    {
        Type = type;
    }

    /// <summary>The entity type the program reads.</summary>
    internal uint Type { get; }

    internal IReadOnlyList<PrcSchemaInstruction> Instructions => instructions;

    /// <summary>Compiles the tokens of <paramref name="type"/>; a token the schema language does not have, or tokens
    /// that end in the middle of a construct, throw as corrupt data at the reader's position.</summary>
    internal static PrcSchemaProgram Compile(uint type, uint[] tokens, PrcBitReader reader)
    {
        var program = new PrcSchemaProgram(type);
        var open = new Stack<PrcSchemaConstruct>();
        var position = 0;
        while (position < tokens.Length)
        {
            var token = (int)Math.Min(tokens[position++], int.MaxValue);
            var expression = program.ExpectsExpression(open);
            switch (token)
            {
                case Boolean or Double or Character or UnsignedInteger or Integer or String or Vector2D or Vector3D
                    or Extent1D or Extent2D or Extent3D:
                    program.Emit(expression ? PrcSchemaCode.ReadValue : PrcSchemaCode.Read, token);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case TypedEntity or PointerToType:
                    program.Emit(PrcSchemaCode.Entity, Literal(tokens, ref position, type, reader));
                    program.PushPlaceholder(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case SurfacePointer or CurvePointer:
                    program.Emit(PrcSchemaCode.Unsupported, token);
                    program.PushPlaceholder(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case FatherType:
                    Literal(tokens, ref position, type, reader);
                    program.PushPlaceholder(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case ValueDeclare:
                    program.Emit(PrcSchemaCode.Declare, Literal(tokens, ref position, type, reader));
                    program.PushPlaceholder(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case Value:
                    program.Emit(PrcSchemaCode.Variable, Literal(tokens, ref position, type, reader));
                    program.PopIfStatement(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case ValueConstant:
                    program.Emit(PrcSchemaCode.Constant, Literal(tokens, ref position, type, reader));
                    program.PopIfStatement(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case ValueFor:
                    program.Emit(PrcSchemaCode.LoopIndex, 0);
                    program.PopIfStatement(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case CurveIs3D:
                    program.Emit(PrcSchemaCode.Constant, 0);
                    program.PopIfStatement(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                case ValueSet or ValueDeclareAndSet:
                    open.Push(new PrcSchemaConstruct(token, Literal(tokens, ref position, type, reader)));
                    break;
                case >= Multiply and <= NotEqual:
                    open.Push(new PrcSchemaConstruct(token, 0));
                    break;
                case For or If:
                    open.Push(new PrcSchemaConstruct(token, 0));
                    break;
                case SimpleFor:
                    program.Emit(PrcSchemaCode.CountedLoopStart, 0);
                    var counted = new PrcSchemaConstruct(token, 0) { LoopTop = program.instructions.Count, Stage = 1 };
                    counted.FirstPending = program.Emit(PrcSchemaCode.LoopTest, 0);
                    open.Push(counted);
                    break;
                case BlockStart:
                    open.Push(new PrcSchemaConstruct(token, 0));
                    break;
                case BlockVersion:
                    var version = new PrcSchemaConstruct(token, Literal(tokens, ref position, type, reader));
                    version.FirstPending = program.Emit(PrcSchemaCode.Version, version.Operand);
                    open.Push(version);
                    break;
                case BlockEnd:
                    if (open.Count == 0 || open.Peek().Kind is not (BlockStart or BlockVersion))
                    {
                        throw reader.Corrupt($"the schema of type {type} ends a block it did not start");
                    }

                    var block = open.Pop();
                    if (block.Kind == BlockVersion)
                    {
                        program.Patch(block.FirstPending);
                    }

                    program.PushPlaceholder(expression);
                    program.Completed(open, tokens, ref position, reader);
                    break;
                default:
                    throw reader.Corrupt($"the schema of type {type} has token {tokens[position - 1]}, which the schema language does not have");
            }
        }

        if (open.Count > 0)
        {
            throw reader.Corrupt($"the schema of type {type} ends inside a construct");
        }

        return program;
    }

    private static double Literal(uint[] tokens, ref int position, uint type, PrcBitReader reader)
    {
        if (position >= tokens.Length)
        {
            throw reader.Corrupt($"the schema of type {type} ends before the value a token needs");
        }

        return tokens[position++];
    }

    private bool ExpectsExpression(Stack<PrcSchemaConstruct> open)
    {
        if (open.Count == 0)
        {
            return false;
        }

        var top = open.Peek();
        return top.Kind switch
        {
            ValueSet or ValueDeclareAndSet => true,
            >= Multiply and <= NotEqual => true,
            For or If => top.Stage == 0,
            _ => false
        };
    }

    private int Emit(PrcSchemaCode code, double operand)
    {
        instructions.Add(new PrcSchemaInstruction(code, operand));
        return instructions.Count - 1;
    }

    private void PushPlaceholder(bool expression)
    {
        if (expression)
        {
            Emit(PrcSchemaCode.Constant, 0);
        }
    }

    private void PopIfStatement(bool expression)
    {
        if (!expression)
        {
            Emit(PrcSchemaCode.Discard, 0);
        }
    }

    private void Patch(int pending)
    {
        instructions[pending] = instructions[pending].WithTarget(instructions.Count);
    }

    private void Completed(Stack<PrcSchemaConstruct> open, uint[] tokens, ref int position, PrcBitReader reader)
    {
        while (open.Count > 0)
        {
            var top = open.Peek();
            switch (top.Kind)
            {
                case BlockStart or BlockVersion:
                    return;
                case ValueSet or ValueDeclareAndSet:
                    Emit(PrcSchemaCode.Store, top.Operand);
                    open.Pop();
                    continue;
                case >= Multiply and <= NotEqual:
                    if (top.Stage++ == 0)
                    {
                        return;
                    }

                    Emit(PrcSchemaCode.Binary, top.Kind);
                    open.Pop();
                    continue;
                case For:
                    if (top.Stage == 0)
                    {
                        top.Stage = 1;
                        Emit(PrcSchemaCode.LoopStart, 0);
                        top.LoopTop = instructions.Count;
                        top.FirstPending = Emit(PrcSchemaCode.LoopTest, 0);
                        return;
                    }

                    CloseLoop(top);
                    open.Pop();
                    continue;
                case SimpleFor:
                    CloseLoop(top);
                    open.Pop();
                    continue;
                case If:
                    if (top.Stage == 0)
                    {
                        top.Stage = 1;
                        top.FirstPending = Emit(PrcSchemaCode.JumpIfFalse, 0);
                        return;
                    }

                    if (top.Stage == 1 && position < tokens.Length && tokens[position] == Else)
                    {
                        position++;
                        top.Stage = 2;
                        top.SecondPending = Emit(PrcSchemaCode.Jump, 0);
                        Patch(top.FirstPending);
                        return;
                    }

                    Patch(top.Stage == 1 ? top.FirstPending : top.SecondPending);
                    open.Pop();
                    continue;
                default:
                    throw reader.Corrupt($"the schema of type {Type} is malformed");
            }
        }
    }

    private void CloseLoop(PrcSchemaConstruct loop)
    {
        instructions.Add(new PrcSchemaInstruction(PrcSchemaCode.LoopNext, 0, loop.LoopTop));
        Patch(loop.FirstPending);
    }
}
