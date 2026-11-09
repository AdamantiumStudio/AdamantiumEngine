using System;
using System.Collections.Generic;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Functions;

/// <summary>Type 4: a program in the PostScript calculator language (ISO 32000-2, 7.10.5). Compiled once into blocks of
/// instructions; nested <c>{ }</c> become blocks that <c>if</c> and <c>ifelse</c> call, run on an explicit stack of
/// frames, so neither compiling nor running recurses. A program that runs past <see cref="MaxSteps"/>, overflows its
/// stack or nests past <see cref="MaxFrames"/> stops, its outputs zero.</summary>
public sealed class PdfPostScriptFunction : PdfFunction
{
    /// <summary>The most operators one evaluation runs; real tint transforms and shadings run a few dozen.</summary>
    public const int MaxSteps = 10_000;

    public const int MaxStack = 100;

    public const int MaxFrames = 1024;

    private readonly List<List<PdfPostScriptInstruction>> _blocks;

    public PdfPostScriptFunction(double[] domain, double[] range, ReadOnlySpan<byte> program) : base(domain, range)
    {
        _blocks = Compile(program);
    }

    public override int Outputs => Range.Length / 2;

    protected override void Compute(ReadOnlySpan<double> input, Span<double> output)
    {
        if (_blocks.Count == 0)
        {
            return;
        }

        var stack = new PdfPostScriptValue[MaxStack];
        var count = 0;
        foreach (var value in input)
        {
            stack[count++] = new PdfPostScriptValue(value);
        }

        if (!Run(stack, ref count))
        {
            return;
        }

        var first = count - output.Length;
        for (var j = 0; j < output.Length; j++)
        {
            output[j] = first + j >= 0 ? stack[first + j].Number : 0;
        }
    }

    private bool Run(PdfPostScriptValue[] stack, ref int count)
    {
        var frames = new Stack<(int Block, int At)>();
        frames.Push((0, 0));
        var steps = 0;
        while (frames.Count > 0)
        {
            var (block, at) = frames.Pop();
            var instructions = _blocks[block];
            if (at >= instructions.Count)
            {
                continue;
            }

            frames.Push((block, at + 1));
            if (++steps > MaxSteps)
            {
                return false;
            }

            var instruction = instructions[at];
            if (instruction.Operator is PdfPostScriptOperator.If or PdfPostScriptOperator.IfElse)
            {
                if (count < 1)
                {
                    return false;
                }

                var next = stack[--count].IsTrue ? instruction.Block : instruction.ElseBlock;
                if (next >= 0)
                {
                    if (frames.Count >= MaxFrames)
                    {
                        return false;
                    }

                    frames.Push((next, 0));
                }

                continue;
            }

            if (!Execute(instruction, stack, ref count))
            {
                return false;
            }
        }

        return true;
    }

    private static int Needs(PdfPostScriptOperator op) => op switch
    {
        PdfPostScriptOperator.Push or PdfPostScriptOperator.True or PdfPostScriptOperator.False => 0,
        PdfPostScriptOperator.Abs or PdfPostScriptOperator.Ceiling or PdfPostScriptOperator.Cos or
            PdfPostScriptOperator.Cvi or PdfPostScriptOperator.Cvr or PdfPostScriptOperator.Floor or
            PdfPostScriptOperator.Ln or PdfPostScriptOperator.Log or PdfPostScriptOperator.Neg or
            PdfPostScriptOperator.Round or PdfPostScriptOperator.Sin or PdfPostScriptOperator.Sqrt or
            PdfPostScriptOperator.Truncate or PdfPostScriptOperator.Not or PdfPostScriptOperator.Dup or
            PdfPostScriptOperator.Pop or PdfPostScriptOperator.Copy or PdfPostScriptOperator.Index => 1,
        _ => 2,
    };

    private static bool Pushes(PdfPostScriptOperator op) =>
        op is PdfPostScriptOperator.Push or PdfPostScriptOperator.True or PdfPostScriptOperator.False or
            PdfPostScriptOperator.Dup or PdfPostScriptOperator.Index;

    private static bool Execute(PdfPostScriptInstruction instruction, PdfPostScriptValue[] stack, ref int count)
    {
        var op = instruction.Operator;
        if (count < Needs(op) || (Pushes(op) && count >= MaxStack))
        {
            return false;
        }

        switch (op)
        {
            case PdfPostScriptOperator.Push:
                stack[count++] = new PdfPostScriptValue(instruction.Value);
                return true;
            case PdfPostScriptOperator.True:
                stack[count++] = PdfPostScriptValue.Of(true);
                return true;
            case PdfPostScriptOperator.False:
                stack[count++] = PdfPostScriptValue.Of(false);
                return true;
            case PdfPostScriptOperator.Dup:
                stack[count] = stack[count - 1];
                count++;
                return true;
            case PdfPostScriptOperator.Pop:
                count--;
                return true;
            case PdfPostScriptOperator.Exch:
                (stack[count - 1], stack[count - 2]) = (stack[count - 2], stack[count - 1]);
                return true;
            case PdfPostScriptOperator.Copy:
                return CopyTop(stack, ref count);
            case PdfPostScriptOperator.Index:
                var n = (int)Math.Clamp(stack[--count].Number, -1, MaxStack);
                if (n < 0 || n >= count)
                {
                    return false;
                }

                stack[count] = stack[count - 1 - n];
                count++;
                return true;
            case PdfPostScriptOperator.Roll:
                return Roll(stack, ref count);
        }

        if (Needs(op) == 1)
        {
            stack[count - 1] = Unary(op, stack[count - 1]);
            return true;
        }

        var right = stack[--count];
        stack[count - 1] = Binary(op, stack[count - 1], right);
        return true;
    }

    private static PdfPostScriptValue Unary(PdfPostScriptOperator op, PdfPostScriptValue value)
    {
        var a = value.Number;
        if (op == PdfPostScriptOperator.Not)
        {
            return value.IsBoolean ? PdfPostScriptValue.Of(!value.IsTrue) : new PdfPostScriptValue(~Integer(a));
        }

        return new PdfPostScriptValue(op switch
        {
            PdfPostScriptOperator.Abs => Math.Abs(a),
            PdfPostScriptOperator.Ceiling => Math.Ceiling(a),
            PdfPostScriptOperator.Cos => Math.Cos(a * Math.PI / 180),
            PdfPostScriptOperator.Cvi or PdfPostScriptOperator.Truncate => Math.Truncate(a),
            PdfPostScriptOperator.Floor => Math.Floor(a),
            PdfPostScriptOperator.Ln => a > 0 ? Math.Log(a) : 0,
            PdfPostScriptOperator.Log => a > 0 ? Math.Log10(a) : 0,
            PdfPostScriptOperator.Neg => -a,
            PdfPostScriptOperator.Round => Math.Floor(a + 0.5),
            PdfPostScriptOperator.Sin => Math.Sin(a * Math.PI / 180),
            PdfPostScriptOperator.Sqrt => a >= 0 ? Math.Sqrt(a) : 0,
            _ => a,
        });
    }

    private static PdfPostScriptValue Binary(PdfPostScriptOperator op, PdfPostScriptValue leftValue,
        PdfPostScriptValue rightValue)
    {
        var left = leftValue.Number;
        var right = rightValue.Number;
        var logical = leftValue.IsBoolean && rightValue.IsBoolean;
        switch (op)
        {
            case PdfPostScriptOperator.And:
                return logical ? PdfPostScriptValue.Of(leftValue.IsTrue && rightValue.IsTrue)
                    : new PdfPostScriptValue(Integer(left) & Integer(right));
            case PdfPostScriptOperator.Or:
                return logical ? PdfPostScriptValue.Of(leftValue.IsTrue || rightValue.IsTrue)
                    : new PdfPostScriptValue(Integer(left) | Integer(right));
            case PdfPostScriptOperator.Xor:
                return logical ? PdfPostScriptValue.Of(leftValue.IsTrue != rightValue.IsTrue)
                    : new PdfPostScriptValue(Integer(left) ^ Integer(right));
            case PdfPostScriptOperator.Eq:
                return PdfPostScriptValue.Of(left == right);
            case PdfPostScriptOperator.Ne:
                return PdfPostScriptValue.Of(left != right);
            case PdfPostScriptOperator.Ge:
                return PdfPostScriptValue.Of(left >= right);
            case PdfPostScriptOperator.Gt:
                return PdfPostScriptValue.Of(left > right);
            case PdfPostScriptOperator.Le:
                return PdfPostScriptValue.Of(left <= right);
            case PdfPostScriptOperator.Lt:
                return PdfPostScriptValue.Of(left < right);
        }

        return new PdfPostScriptValue(op switch
        {
            PdfPostScriptOperator.Add => left + right,
            PdfPostScriptOperator.Sub => left - right,
            PdfPostScriptOperator.Mul => left * right,
            PdfPostScriptOperator.Div => right == 0 ? 0 : left / right,
            PdfPostScriptOperator.Idiv => IntegerDivide(left, right),
            PdfPostScriptOperator.Mod => Math.Truncate(right) == 0 ? 0
                : Math.Truncate(left) - Math.Truncate(right) * IntegerDivide(left, right),
            PdfPostScriptOperator.Exp => Math.Pow(left, right) is var power && double.IsFinite(power) ? power : 0,
            PdfPostScriptOperator.Atan => Atan(left, right),
            PdfPostScriptOperator.Bitshift => right >= 0 ? Integer(left) << (int)Math.Min(right, 63)
                : Integer(left) >> (int)Math.Min(-right, 63),
            _ => left,
        });
    }

    private static double IntegerDivide(double left, double right) =>
        Math.Truncate(right) == 0 ? 0 : Math.Truncate(Math.Truncate(left) / Math.Truncate(right));

    private static long Integer(double value) =>
        double.IsNaN(value) ? 0 : value >= long.MaxValue ? long.MaxValue : value <= long.MinValue ? long.MinValue
            : (long)value;

    private static double Atan(double numerator, double denominator)
    {
        var degrees = Math.Atan2(numerator, denominator) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static bool CopyTop(PdfPostScriptValue[] stack, ref int count)
    {
        var n = (int)Math.Clamp(stack[--count].Number, -1, MaxStack + 1);
        if (n < 0 || n > count || count + n > MaxStack)
        {
            return false;
        }

        Array.Copy(stack, count - n, stack, count, n);
        count += n;
        return true;
    }

    private static bool Roll(PdfPostScriptValue[] stack, ref int count)
    {
        var j = (int)Math.Clamp(stack[--count].Number, int.MinValue / 2, int.MaxValue / 2);
        var n = (int)Math.Clamp(stack[--count].Number, -1, MaxStack + 1);
        if (n < 0 || n > count)
        {
            return false;
        }

        if (n == 0)
        {
            return true;
        }

        j = ((j % n) + n) % n;
        var segment = stack.AsSpan(count - n, n);
        segment.Reverse();
        segment[..j].Reverse();
        segment[j..].Reverse();
        return true;
    }

    private static List<List<PdfPostScriptInstruction>> Compile(ReadOnlySpan<byte> program)
    {
        var blocks = new List<List<PdfPostScriptInstruction>>();
        var open = new Stack<int>();
        var pending = new Stack<List<int>>();
        var lexer = new PdfLexer(program.ToArray());
        while (true)
        {
            var token = lexer.Next();
            switch (token.Kind)
            {
                case PdfTokenKind.EndOfData:
                    return blocks;
                case PdfTokenKind.ProcedureStart:
                    blocks.Add([]);
                    open.Push(blocks.Count - 1);
                    pending.Push([]);
                    continue;
                case PdfTokenKind.ProcedureEnd:
                    if (open.Count == 0)
                    {
                        continue;
                    }

                    var closed = open.Pop();
                    pending.Pop();
                    if (open.Count == 0)
                    {
                        return blocks;
                    }

                    pending.Peek().Add(closed);
                    continue;
            }

            if (open.Count == 0)
            {
                continue;
            }

            var current = blocks[open.Peek()];
            var pendingBlocks = pending.Peek();
            if (token.Kind is PdfTokenKind.Integer or PdfTokenKind.Real)
            {
                current.Add(new PdfPostScriptInstruction(PdfPostScriptOperator.Push, token.Real));
                pendingBlocks.Clear();
                continue;
            }

            if (token.Kind != PdfTokenKind.Keyword || !TryOperator(token.Text, out var op))
            {
                pendingBlocks.Clear();
                continue;
            }

            if (op == PdfPostScriptOperator.If && pendingBlocks.Count >= 1)
            {
                current.Add(new PdfPostScriptInstruction(op, Block: pendingBlocks[^1]));
            }
            else if (op == PdfPostScriptOperator.IfElse && pendingBlocks.Count >= 2)
            {
                current.Add(new PdfPostScriptInstruction(op, Block: pendingBlocks[^2], ElseBlock: pendingBlocks[^1]));
            }
            else if (op is not (PdfPostScriptOperator.If or PdfPostScriptOperator.IfElse))
            {
                current.Add(new PdfPostScriptInstruction(op));
            }

            pendingBlocks.Clear();
        }
    }

    private static bool TryOperator(string text, out PdfPostScriptOperator op)
    {
        if (text.Length == 0 || !char.IsLetter(text[0]) || !Enum.TryParse(text, true, out op) ||
            op is PdfPostScriptOperator.Push)
        {
            op = default;
            return false;
        }

        return true;
    }
}
