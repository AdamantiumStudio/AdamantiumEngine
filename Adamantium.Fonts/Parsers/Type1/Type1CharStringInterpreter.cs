using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Encodings;
using Adamantium.Fonts.Parsers.CFF;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>Runs a decrypted Type 1 charstring and writes the outline it draws as Type 2 moves, lines and curves, the
/// form <see cref="Glyph.FillOutlines"/> reads. Subroutines run on an explicit call stack; flex, hint replacement and
/// seac accents are followed, hints are dropped.</summary>
internal sealed class Type1CharStringInterpreter
{
    private const int MaxStack = 48;
    private const int MaxCallDepth = 32;
    private const int MaxSteps = 200_000;
    private const int MaxStepsToWidth = 64;
    private const int FlexPointCount = 7;

    private readonly IReadOnlyDictionary<int, byte[]> _subrs;
    private readonly Func<string, byte[]> _charStringByName;
    private readonly FontWorkBudget _budget;

    private readonly double[] _stack = new double[MaxStack];
    private readonly Stack<Type1CallFrame> _calls = new();
    private readonly Stack<double> _postScriptStack = new();
    private readonly List<(double X, double Y)> _flexPoints = new();

    private int _count;
    private int _steps;
    private double _x;
    private double _y;
    private double _offsetX;
    private double _offsetY;
    private double _lastX;
    private double _lastY;
    private bool _open;
    private bool _flexing;
    private double _flexStartX;
    private double _flexStartY;

    public Type1CharStringInterpreter(IReadOnlyDictionary<int, byte[]> subrs, Func<string, byte[]> charStringByName,
        FontWorkBudget budget)
    {
        _subrs = subrs;
        _charStringByName = charStringByName;
        _budget = budget;
    }

    public List<Command> Commands { get; private set; }

    /// <summary>The advance width the charstring sets with hsbw or sbw.</summary>
    public double Width { get; private set; }

    public double SideBearingX { get; private set; }

    /// <summary>Draws the glyph of <paramref name="charString"/>, or with <paramref name="widthOnly"/> stops once its
    /// width is known.</summary>
    /// <exception cref="InvalidDataException">The charstring is malformed.</exception>
    public void Run(byte[] charString, bool widthOnly = false)
    {
        Commands = new List<Command>();
        Width = 0;
        SideBearingX = 0;
        _steps = 0;
        _lastX = 0;
        _lastY = 0;

        var accent = RunComponent(charString, 0, 0, false, widthOnly);
        if (accent is not { } seac || widthOnly)
        {
            return;
        }

        var baseCode = _charStringByName(StandardEncoding.NameOf(seac.BaseCode));
        var accentCode = _charStringByName(StandardEncoding.NameOf(seac.AccentCode));
        if (baseCode == null || accentCode == null)
        {
            throw new InvalidDataException("An accented glyph names a character the font does not have.");
        }

        RunComponent(baseCode, 0, 0, true, false);
        RunComponent(accentCode, SideBearingX + seac.AccentX - seac.AccentSideBearing, seac.AccentY, true, false);
    }

    private (double AccentSideBearing, double AccentX, double AccentY, int BaseCode, int AccentCode)?
        RunComponent(byte[] code, double offsetX, double offsetY, bool isComponent, bool widthOnly)
    {
        _count = 0;
        _calls.Clear();
        _postScriptStack.Clear();
        _flexPoints.Clear();
        _flexing = false;
        _open = false;
        _x = 0;
        _y = 0;
        _offsetX = offsetX;
        _offsetY = offsetY;

        var data = code;
        var position = 0;
        while (true)
        {
            if (position >= data.Length)
            {
                if (_calls.Count == 0)
                {
                    return null;
                }

                var frame = _calls.Pop();
                data = frame.Code;
                position = frame.Position;
                continue;
            }

            if (++_steps > (widthOnly ? MaxStepsToWidth : MaxSteps))
            {
                if (widthOnly)
                {
                    return null;
                }

                throw new InvalidDataException("A charstring runs too long.");
            }

            _budget.Spend(1);
            int b = data[position++];
            if (b >= 32)
            {
                Push(ReadNumber(b, data, ref position));
                continue;
            }

            switch ((Type1Operator)b)
            {
                case Type1Operator.VMoveTo:
                    MoveTo(0, Arg(1, 0));
                    break;
                case Type1Operator.RLineTo:
                    LineTo(Arg(2, 0), Arg(2, 1));
                    break;
                case Type1Operator.HLineTo:
                    LineTo(Arg(1, 0), 0);
                    break;
                case Type1Operator.VLineTo:
                    LineTo(0, Arg(1, 0));
                    break;
                case Type1Operator.RRCurveTo:
                    CurveTo(Arg(6, 0), Arg(6, 1), Arg(6, 2), Arg(6, 3), Arg(6, 4), Arg(6, 5));
                    break;
                case Type1Operator.ClosePath:
                    _open = false;
                    _count = 0;
                    break;
                case Type1Operator.CallSubr:
                {
                    var index = (int)Pop();
                    if (!_subrs.TryGetValue(index, out var subr))
                    {
                        throw new InvalidDataException($"A charstring calls subroutine {index}, which the font lacks.");
                    }

                    if (_calls.Count >= MaxCallDepth)
                    {
                        throw new InvalidDataException("Charstring subroutines nest too deep.");
                    }

                    _calls.Push(new Type1CallFrame(data, position));
                    data = subr;
                    position = 0;
                    break;
                }
                case Type1Operator.Return:
                {
                    if (_calls.Count == 0)
                    {
                        return null;
                    }

                    var frame = _calls.Pop();
                    data = frame.Code;
                    position = frame.Position;
                    break;
                }
                case Type1Operator.Escape:
                {
                    if (position >= data.Length)
                    {
                        throw new InvalidDataException("A charstring ends inside an operator.");
                    }

                    switch ((Type1EscapeOperator)data[position++])
                    {
                        case Type1EscapeOperator.Seac:
                            if (isComponent)
                            {
                                throw new InvalidDataException("An accent component is itself accented.");
                            }

                            return (Arg(5, 0), Arg(5, 1), Arg(5, 2), (int)Arg(5, 3), (int)Arg(5, 4));
                        case Type1EscapeOperator.Sbw:
                            SetSideBearing(Arg(4, 0), Arg(4, 1), Arg(4, 2), isComponent);
                            if (widthOnly)
                            {
                                return null;
                            }

                            break;
                        case Type1EscapeOperator.Div:
                        {
                            var divisor = Pop();
                            var dividend = Pop();
                            if (divisor == 0)
                            {
                                throw new InvalidDataException("A charstring divides by zero.");
                            }

                            Push(dividend / divisor);
                            break;
                        }
                        case Type1EscapeOperator.CallOtherSubr:
                            CallOtherSubr();
                            break;
                        case Type1EscapeOperator.Pop:
                            if (_postScriptStack.Count == 0)
                            {
                                throw new InvalidDataException("A charstring pops an empty PostScript stack.");
                            }

                            Push(_postScriptStack.Pop());
                            break;
                        case Type1EscapeOperator.SetCurrentPoint:
                            _x = Arg(2, 0);
                            _y = Arg(2, 1);
                            _count = 0;
                            break;
                        default:
                            _count = 0;
                            break;
                    }

                    break;
                }
                case Type1Operator.Hsbw:
                    SetSideBearing(Arg(2, 0), 0, Arg(2, 1), isComponent);
                    if (widthOnly)
                    {
                        return null;
                    }

                    break;
                case Type1Operator.EndChar:
                    return null;
                case Type1Operator.RMoveTo:
                    MoveTo(Arg(2, 0), Arg(2, 1));
                    break;
                case Type1Operator.HMoveTo:
                    MoveTo(Arg(1, 0), 0);
                    break;
                case Type1Operator.VHCurveTo:
                    CurveTo(0, Arg(4, 0), Arg(4, 1), Arg(4, 2), Arg(4, 3), 0);
                    break;
                case Type1Operator.HVCurveTo:
                    CurveTo(Arg(4, 0), 0, Arg(4, 1), Arg(4, 2), 0, Arg(4, 3));
                    break;
                default:
                    _count = 0;
                    break;
            }
        }
    }

    private void SetSideBearing(double x, double y, double width, bool isComponent)
    {
        if (!isComponent)
        {
            Width = width;
            SideBearingX = x;
        }

        _x = x;
        _y = y;
        _count = 0;
    }

    private void CallOtherSubr()
    {
        var number = (int)Pop();
        var argumentCount = (int)Pop();
        if (argumentCount < 0 || argumentCount > _count)
        {
            throw new InvalidDataException("A charstring passes more arguments than it holds.");
        }

        var first = _count - argumentCount;
        _count = first;
        _postScriptStack.Clear();
        switch ((Type1OtherSubr)number)
        {
            case Type1OtherSubr.FlexEnd when argumentCount == 3:
                EndFlex();
                _postScriptStack.Push(_stack[first + 2]);
                _postScriptStack.Push(_stack[first + 1]);
                return;
            case Type1OtherSubr.FlexStart:
                _flexing = true;
                _flexPoints.Clear();
                _flexStartX = _x;
                _flexStartY = _y;
                return;
            case Type1OtherSubr.FlexPoint:
                if (_flexing && _flexPoints.Count < FlexPointCount)
                {
                    _flexPoints.Add((_x, _y));
                }

                return;
        }

        for (var i = first + argumentCount - 1; i >= first; i--)
        {
            _postScriptStack.Push(_stack[i]);
        }
    }

    private void EndFlex()
    {
        _flexing = false;
        if (_flexPoints.Count == 0)
        {
            return;
        }

        StartPath(_flexStartX, _flexStartY);
        if (_flexPoints.Count == FlexPointCount)
        {
            EmitCurve(_flexPoints[1], _flexPoints[2], _flexPoints[3]);
            EmitCurve(_flexPoints[4], _flexPoints[5], _flexPoints[6]);
        }
        else
        {
            Emit(OperatorsType.rlineto, _flexPoints[^1].X + _offsetX, _flexPoints[^1].Y + _offsetY);
        }

        (_x, _y) = _flexPoints[^1];
    }

    private void MoveTo(double dx, double dy)
    {
        _x += dx;
        _y += dy;
        _count = 0;
        if (!_flexing)
        {
            _open = false;
        }
    }

    private void LineTo(double dx, double dy)
    {
        StartPath(_x, _y);
        _x += dx;
        _y += dy;
        _count = 0;
        Emit(OperatorsType.rlineto, _x + _offsetX, _y + _offsetY);
    }

    private void CurveTo(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
    {
        StartPath(_x, _y);
        var first = (_x + dx1, _y + dy1);
        var second = (first.Item1 + dx2, first.Item2 + dy2);
        var end = (second.Item1 + dx3, second.Item2 + dy3);
        _count = 0;
        EmitCurve(first, second, end);
        (_x, _y) = end;
    }

    private void StartPath(double x, double y)
    {
        if (_open)
        {
            return;
        }

        Emit(OperatorsType.rmoveto, x + _offsetX, y + _offsetY);
        _open = true;
    }

    private void EmitCurve((double X, double Y) first, (double X, double Y) second, (double X, double Y) end)
    {
        var x1 = first.X + _offsetX;
        var y1 = first.Y + _offsetY;
        var x2 = second.X + _offsetX;
        var y2 = second.Y + _offsetY;
        var x3 = end.X + _offsetX;
        var y3 = end.Y + _offsetY;
        Commands.Add(new Command
        {
            Operator = OperatorsType.rrcurveto,
            Operands =
            [
                new CommandOperand(x1 - _lastX), new CommandOperand(y1 - _lastY),
                new CommandOperand(x2 - x1), new CommandOperand(y2 - y1),
                new CommandOperand(x3 - x2), new CommandOperand(y3 - y2),
            ],
        });
        _lastX = x3;
        _lastY = y3;
    }

    private void Emit(OperatorsType op, double x, double y)
    {
        Commands.Add(new Command
        {
            Operator = op,
            Operands = [new CommandOperand(x - _lastX), new CommandOperand(y - _lastY)],
        });
        _lastX = x;
        _lastY = y;
    }

    private double Arg(int count, int index)
    {
        if (_count < count)
        {
            throw new InvalidDataException("A charstring operator lacks its arguments.");
        }

        return _stack[_count - count + index];
    }

    private void Push(double value)
    {
        if (_count >= MaxStack)
        {
            throw new InvalidDataException("A charstring overflows its argument stack.");
        }

        _stack[_count++] = value;
    }

    private double Pop()
    {
        if (_count == 0)
        {
            throw new InvalidDataException("A charstring operator lacks its arguments.");
        }

        return _stack[--_count];
    }

    private static double ReadNumber(int b, byte[] data, ref int position)
    {
        if (b <= 246)
        {
            return b - 139;
        }

        if (b <= 254)
        {
            if (position >= data.Length)
            {
                throw new InvalidDataException("A charstring ends inside a number.");
            }

            var w = data[position++];
            return b <= 250 ? (b - 247) * 256 + w + 108 : -(b - 251) * 256 - w - 108;
        }

        if (position > data.Length - 4)
        {
            throw new InvalidDataException("A charstring ends inside a number.");
        }

        var value = (data[position] << 24) | (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
        position += 4;
        return value;
    }
}
