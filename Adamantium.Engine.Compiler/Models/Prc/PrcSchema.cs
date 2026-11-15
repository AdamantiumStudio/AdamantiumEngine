using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The schema a section starts with (PRC "Serialize Schema"): per entity type, the tokens that read what versions
/// newer than this reader added, run without recursion.</summary>
internal sealed class PrcSchema
{
    /// <summary>The version of the specification this reader reads natively; versioned blocks up to it are its own.</summary>
    internal const uint ReaderVersion = 8137;

    private const int MaximumCallDepth = 64;
    private const long MaximumSteps = 50_000_000;

    private readonly Dictionary<uint, PrcSchemaProgram> _programs = new();

    /// <summary>An empty schema: nothing to step over.</summary>
    internal static PrcSchema Empty { get; } = new();

    /// <summary>The entity types the schema describes.</summary>
    internal IReadOnlyCollection<uint> Types => _programs.Keys;

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

            schema._programs[type] = PrcSchemaProgram.Compile(type, tokens, reader);
        }

        return schema;
    }

    /// <summary>Whether the schema says how to read entities of <paramref name="type"/>.</summary>
    internal bool HasType(uint type) => _programs.ContainsKey(type);

    /// <summary>Steps over what versions newer than this reader added to an entity of <paramref name="type"/>, whose
    /// own data has just been read.</summary>
    internal void SkipNewData(PrcBitReader reader, uint type)
    {
        if (_programs.TryGetValue(type, out var program))
        {
            Run(reader, program, false);
        }
    }

    /// <summary>Steps over a whole entity of <paramref name="type"/>, a type this reader does not know; its type code
    /// has been read. Throws when the schema does not describe the type: nothing then says how long the entity is.</summary>
    internal void SkipEntity(PrcBitReader reader, uint type)
    {
        if (!_programs.TryGetValue(type, out var program))
        {
            throw reader.CreateCorruptException($"it holds an entity of type {type}, which neither this reader nor the file's schema knows");
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
                throw reader.CreateCorruptException($"the schema of type {first.Type} runs past {MaximumSteps} steps");
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
                    call.Loops.Push(new PrcSchemaLoop(GetLoopCount(Pop(values, reader), reader)));
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
                    if (!call.IsWholeEntity && instruction.Operand <= ReaderVersion)
                    {
                        call.Position = instruction.Target;
                    }

                    break;
                case PrcSchemaCode.Entity:
                    var type = (uint)instruction.Operand;
                    if (!_programs.TryGetValue(type, out var entity))
                    {
                        throw reader.CreateCorruptException($"the schema of type {call.Program.Type} reads an entity of type {type}, " +
                                                            "which only a schema of the file could say how to read and none does");
                    }

                    if (calls.Count >= MaximumCallDepth)
                    {
                        throw reader.CreateCorruptException($"the schema entities nest more than {MaximumCallDepth} deep");
                    }

                    calls.Push(new PrcSchemaCall(entity, true));
                    break;
                case PrcSchemaCode.Unsupported:
                    throw reader.CreateCorruptException($"the schema of type {call.Program.Type} reads a {Describe((int)instruction.Operand)}, " +
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
                throw reader.CreateCorruptException($"the schema reads a value of kind {kind}");
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
            throw reader.CreateCorruptException("a schema expression lacks a value");
        }

        return values.Pop();
    }

    private static uint GetLoopCount(double count, PrcBitReader reader)
    {
        if (count < 0 || count > uint.MaxValue || double.IsNaN(count))
        {
            throw reader.CreateCorruptException($"a schema loop runs {count} times");
        }

        var whole = (uint)count;
        reader.CheckCount(whole, 1, "schema loop turns");
        return whole;
    }

    private static double Apply(int token, double left, double right, PrcBitReader reader) => token switch
    {
        PrcSchemaProgram.Multiply => left * right,
        PrcSchemaProgram.Divide => right == 0 ? throw reader.CreateCorruptException("a schema expression divides by zero") : Math.Truncate(left / right),
        PrcSchemaProgram.Add => left + right,
        PrcSchemaProgram.Subtract => left - right,
        PrcSchemaProgram.Less => left < right ? 1 : 0,
        PrcSchemaProgram.LessOrEqual => left <= right ? 1 : 0,
        PrcSchemaProgram.Greater => left > right ? 1 : 0,
        PrcSchemaProgram.GreaterOrEqual => left >= right ? 1 : 0,
        PrcSchemaProgram.Equal => left == right ? 1 : 0,
        PrcSchemaProgram.NotEqual => left != right ? 1 : 0,
        _ => throw reader.CreateCorruptException($"schema operator {token} is not known")
    };

    private static string Describe(int token) => token switch
    {
        PrcSchemaProgram.SurfacePointer => "surface",
        PrcSchemaProgram.CurvePointer => "curve",
        _ => $"token {token}"
    };
}
