using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>One entity type's schema tokens compiled into instructions; token values are those the files carry.</summary>
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

    private readonly List<PrcSchemaInstruction> _instructions = [];

    private PrcSchemaProgram(uint type)
    {
        Type = type;
    }

    /// <summary>The entity type the program reads.</summary>
    internal uint Type { get; }

    internal IReadOnlyList<PrcSchemaInstruction> Instructions => _instructions;

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
            var expression = program.IsExpressionExpected(open);
            switch (token)
            {
                case Boolean or Double or Character or UnsignedInteger or Integer or String or Vector2D or Vector3D
                    or Extent1D or Extent2D or Extent3D:
                    program.Emit(expression ? PrcSchemaCode.ReadValue : PrcSchemaCode.Read, token);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case TypedEntity or PointerToType:
                    program.Emit(PrcSchemaCode.Entity, ReadLiteral(tokens, ref position, type, reader));
                    program.PushPlaceholder(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case SurfacePointer or CurvePointer:
                    program.Emit(PrcSchemaCode.Unsupported, token);
                    program.PushPlaceholder(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case FatherType:
                    ReadLiteral(tokens, ref position, type, reader);
                    program.PushPlaceholder(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case ValueDeclare:
                    program.Emit(PrcSchemaCode.Declare, ReadLiteral(tokens, ref position, type, reader));
                    program.PushPlaceholder(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case Value:
                    program.Emit(PrcSchemaCode.Variable, ReadLiteral(tokens, ref position, type, reader));
                    program.PopIfStatement(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case ValueConstant:
                    program.Emit(PrcSchemaCode.Constant, ReadLiteral(tokens, ref position, type, reader));
                    program.PopIfStatement(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case ValueFor:
                    program.Emit(PrcSchemaCode.LoopIndex, 0);
                    program.PopIfStatement(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case CurveIs3D:
                    program.Emit(PrcSchemaCode.Constant, 0);
                    program.PopIfStatement(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                case ValueSet or ValueDeclareAndSet:
                    open.Push(new PrcSchemaConstruct(token, ReadLiteral(tokens, ref position, type, reader)));
                    break;
                case >= Multiply and <= NotEqual:
                    open.Push(new PrcSchemaConstruct(token, 0));
                    break;
                case For or If:
                    open.Push(new PrcSchemaConstruct(token, 0));
                    break;
                case SimpleFor:
                    program.Emit(PrcSchemaCode.CountedLoopStart, 0);
                    var counted = new PrcSchemaConstruct(token, 0) { LoopTop = program._instructions.Count, Stage = 1 };
                    counted.FirstPending = program.Emit(PrcSchemaCode.LoopTest, 0);
                    open.Push(counted);
                    break;
                case BlockStart:
                    open.Push(new PrcSchemaConstruct(token, 0));
                    break;
                case BlockVersion:
                    var version = new PrcSchemaConstruct(token, ReadLiteral(tokens, ref position, type, reader));
                    version.FirstPending = program.Emit(PrcSchemaCode.Version, version.Operand);
                    open.Push(version);
                    break;
                case BlockEnd:
                    if (open.Count == 0 || open.Peek().Kind is not (BlockStart or BlockVersion))
                    {
                        throw reader.CreateCorruptException($"the schema of type {type} ends a block it did not start");
                    }

                    var block = open.Pop();
                    if (block.Kind == BlockVersion)
                    {
                        program.Patch(block.FirstPending);
                    }

                    program.PushPlaceholder(expression);
                    program.CompleteConstructs(open, tokens, ref position, reader);
                    break;
                default:
                    throw reader.CreateCorruptException($"the schema of type {type} has token {tokens[position - 1]}, which the schema language does not have");
            }
        }

        if (open.Count > 0)
        {
            throw reader.CreateCorruptException($"the schema of type {type} ends inside a construct");
        }

        return program;
    }

    private static double ReadLiteral(uint[] tokens, ref int position, uint type, PrcBitReader reader)
    {
        if (position >= tokens.Length)
        {
            throw reader.CreateCorruptException($"the schema of type {type} ends before the value a token needs");
        }

        return tokens[position++];
    }

    private bool IsExpressionExpected(Stack<PrcSchemaConstruct> open)
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
        _instructions.Add(new PrcSchemaInstruction(code, operand));
        return _instructions.Count - 1;
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
        var instruction = _instructions[pending];
        _instructions[pending] = new PrcSchemaInstruction(instruction.Code, instruction.Operand, _instructions.Count);
    }

    private void CompleteConstructs(Stack<PrcSchemaConstruct> open, uint[] tokens, ref int position, PrcBitReader reader)
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
                        top.LoopTop = _instructions.Count;
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
                    throw reader.CreateCorruptException($"the schema of type {Type} is malformed");
            }
        }
    }

    private void CloseLoop(PrcSchemaConstruct loop)
    {
        _instructions.Add(new PrcSchemaInstruction(PrcSchemaCode.LoopNext, 0, loop.LoopTop));
        Patch(loop.FirstPending);
    }
}
